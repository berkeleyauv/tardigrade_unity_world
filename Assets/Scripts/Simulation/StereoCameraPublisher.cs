using System;
using System.Collections;
using UnityEngine;
using Unity.Robotics.ROSTCPConnector;
using RosMessageTypes.BuiltinInterfaces;
using RosMessageTypes.Sensor;
using RosMessageTypes.Std;

namespace Tardigrade.Simulation
{
    /// <summary>Standard ROS stereo images; no native ZED SDK is required.</summary>
    public sealed class StereoCameraPublisher : MonoBehaviour
    {
        const string Prefix = "/tardigrade/sensors/camera/front";
        ROSConnection ros;
        Func<double> clock;
        SensorConfig config;
        Camera leftCamera, rightCamera;
        RenderTexture leftTarget, rightTarget;
        Texture2D readback;
        Material cameraEffect;
        [SerializeField] Transform stereoRig;
        [SerializeField] Camera previewCamera;
        float effectSeed;
        double effectEpoch;
        double nextSample;

        public void SetAuthoredRig(Transform authoredRig, Camera authoredPreviewCamera)
        {
            stereoRig = authoredRig;
            previewCamera = authoredPreviewCamera;
        }

        public void Initialize(SimulationConfig simulation, ROSConnection connection, Func<double> simulationClock)
        {
            config = simulation.Sensor("front_stereo");
            ros = connection;
            clock = simulationClock;
            Camera source = previewCamera != null ? previewCamera : GetComponentInChildren<Camera>();
            leftCamera = CreateCamera("zed_left_optical", source, +config.baseline_m * 0.5f);
            rightCamera = CreateCamera("zed_right_optical", source, -config.baseline_m * 0.5f);
            int width = config.resolution[0], height = config.resolution[1];
            leftTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            rightTarget = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            leftCamera.targetTexture = leftTarget;
            rightCamera.targetTexture = rightTarget;
            readback = new Texture2D(width, height, TextureFormat.RGB24, false);
            Shader effectShader = Resources.Load<Shader>("UnderwaterCamera");
            if (effectShader != null)
                cameraEffect = new Material(effectShader) { name = "Synthetic ZED underwater response" };
            // Camera data is intentionally latest-frame data; a short queue avoids
            // minutes of stale images accumulating when the endpoint is slow.
            ros.RegisterPublisher<ImageMsg>(Prefix + "/left/image_raw", 2);
            ros.RegisterPublisher<CameraInfoMsg>(Prefix + "/left/camera_info", 2);
            ros.RegisterPublisher<ImageMsg>(Prefix + "/right/image_raw", 2);
            ros.RegisterPublisher<CameraInfoMsg>(Prefix + "/right/camera_info", 2);
            StartCoroutine(PublishLoop());
        }

        public void ConfigureVisualProfile(string scenarioId, uint seed)
        {
            effectSeed = (seed % 65521u) * .0137f;
            effectEpoch = clock != null ? clock() : 0.0;
            if (cameraEffect == null)
                return;
            bool lowVisibility = scenarioId == "low_visibility";
            bool clean = scenarioId == "clean";
            bool stress = scenarioId == "sensor_stress";
            cameraEffect.SetColor("_WaterTint", lowVisibility ?
                new Color(.50f, .84f, .80f, 1f) :
                clean ? new Color(.90f, .99f, 1.01f, 1f) : new Color(.72f, .96f, 1.02f, 1f));
            cameraEffect.SetColor("_HazeColor", lowVisibility ?
                new Color(.012f, .075f, .065f, 1f) : new Color(.022f, .145f, .18f, 1f));
            cameraEffect.SetFloat("_Clarity", lowVisibility ? .64f : clean ? .97f : .88f);
            cameraEffect.SetFloat("_Noise", stress ? .026f : lowVisibility ? .018f : clean ? .0025f : .008f);
            cameraEffect.SetFloat("_Vignette", .15f);
        }

        Camera CreateCamera(string name, Camera source, float rosY)
        {
            var cameraObject = new GameObject(name);
            Transform parent = stereoRig != null ? stereoRig : transform;
            cameraObject.transform.SetParent(parent, false);
            if (stereoRig != null)
                cameraObject.transform.localPosition = SimulationConfig.RosPolarToUnity(
                    new[] { 0f, rosY, 0f });
            else
            {
                float[] p = config.position_m;
                cameraObject.transform.localPosition = SimulationConfig.RosPolarToUnity(
                    new[] { p[0], p[1] + rosY, p[2] });
            }
            cameraObject.transform.localRotation = Quaternion.identity;
            Camera camera = cameraObject.AddComponent<Camera>();
            if (source != null)
                camera.CopyFrom(source);
            camera.fieldOfView = VerticalFov(config.horizontal_fov_deg,
                config.resolution[0], config.resolution[1]);
            camera.enabled = false;
            return camera;
        }

        IEnumerator PublishLoop()
        {
            while (true)
            {
                yield return new WaitForEndOfFrame();
                if (ros == null || ros.HasConnectionError)
                    continue;
                if (clock() + 1e-9 < nextSample)
                    continue;
                double sampleTime = clock();
                nextSample = sampleTime + 1.0 / Mathf.Max(config.rate_hz, 1f);
                PublishCamera(leftCamera, leftTarget, "zed_left_camera_optical_frame",
                    Prefix + "/left/image_raw", Prefix + "/left/camera_info", sampleTime, 0.0, 0f);
                PublishCamera(rightCamera, rightTarget, "zed_right_camera_optical_frame",
                    Prefix + "/right/image_raw", Prefix + "/right/camera_info", sampleTime,
                    -config.baseline_m, 19.19f);
            }
        }

        void PublishCamera(Camera camera, RenderTexture target, string frame,
            string imageTopic, string infoTopic, double sampleTime, double projectionTx,
            float eyeNoiseOffset)
        {
            camera.Render();
            RenderTexture previous = RenderTexture.active;
            RenderTexture processed = null;
            if (cameraEffect != null)
            {
                processed = RenderTexture.GetTemporary(target.descriptor);
                cameraEffect.SetFloat("_FrameSeed", effectSeed + eyeNoiseOffset +
                    (float)(sampleTime - effectEpoch) * 37.1f);
                Graphics.Blit(target, processed, cameraEffect);
            }
            RenderTexture.active = processed != null ? processed : target;
            readback.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0, false);
            readback.Apply(false);
            RenderTexture.active = previous;
            if (processed != null)
                RenderTexture.ReleaseTemporary(processed);
            byte[] raw = readback.GetRawTextureData();
            byte[] topDown = new byte[raw.Length];
            int rowBytes = target.width * 3;
            for (int row = 0; row < target.height; ++row)
                Buffer.BlockCopy(raw, row * rowBytes, topDown,
                    (target.height - row - 1) * rowBytes, rowBytes);
            HeaderMsg header = new HeaderMsg(ToRosTime(sampleTime), frame);
            var image = new ImageMsg(header, (uint)target.height, (uint)target.width,
                "rgb8", 0, (uint)rowBytes, topDown);
            ros.Publish(imageTopic, image);
            ros.Publish(infoTopic, CameraInfo(header, target.width, target.height, projectionTx));
        }

        CameraInfoMsg CameraInfo(HeaderMsg header, int width, int height, double tx)
        {
            double fx = width / (2.0 * Math.Tan(config.horizontal_fov_deg * Mathf.Deg2Rad * 0.5));
            double fy = fx;
            double cx = (width - 1) * 0.5;
            double cy = (height - 1) * 0.5;
            var info = new CameraInfoMsg();
            info.header = header;
            info.height = (uint)height;
            info.width = (uint)width;
            info.distortion_model = "plumb_bob";
            info.d = new double[5];
            info.k = new[] { fx, 0.0, cx, 0.0, fy, cy, 0.0, 0.0, 1.0 };
            info.r = new[] { 1.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 1.0 };
            info.p = new[] { fx, 0.0, cx, fx * tx, 0.0, fy, cy, 0.0, 0.0, 0.0, 1.0, 0.0 };
            return info;
        }

        static float VerticalFov(float horizontal, int width, int height) =>
            2f * Mathf.Atan(Mathf.Tan(horizontal * Mathf.Deg2Rad * .5f) * height / width) * Mathf.Rad2Deg;

        static TimeMsg ToRosTime(double seconds)
        {
            int whole = (int)Math.Floor(seconds);
            uint nanos = (uint)Math.Round((seconds - whole) * 1e9);
            if (nanos >= 1000000000) { ++whole; nanos -= 1000000000; }
            return new TimeMsg(whole, nanos);
        }

        void OnDestroy()
        {
            if (leftTarget != null) leftTarget.Release();
            if (rightTarget != null) rightTarget.Release();
            if (readback != null) Destroy(readback);
            if (cameraEffect != null) Destroy(cameraEffect);
        }
    }
}
