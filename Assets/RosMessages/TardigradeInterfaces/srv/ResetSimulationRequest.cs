// Generated from tardigrade_interfaces/srv/ResetSimulation.srv.
using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.TardigradeInterfaces
{
    [Serializable]
    public class ResetSimulationRequest : Message
    {
        public const string k_RosMessageName = "tardigrade_interfaces/ResetSimulation";
        public override string RosMessageName => k_RosMessageName;

        public string scenario_id;
        public uint seed;
        public Geometry.PoseMsg initial_pose;

        public ResetSimulationRequest()
        {
            scenario_id = "";
            seed = 0;
            initial_pose = new Geometry.PoseMsg();
        }

        public static ResetSimulationRequest Deserialize(MessageDeserializer deserializer) =>
            new ResetSimulationRequest(deserializer);

        ResetSimulationRequest(MessageDeserializer deserializer)
        {
            deserializer.Read(out scenario_id);
            deserializer.Read(out seed);
            initial_pose = Geometry.PoseMsg.Deserialize(deserializer);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(scenario_id);
            serializer.Write(seed);
            serializer.Write(initial_pose);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register() => MessageRegistry.Register(k_RosMessageName, Deserialize);
    }
}
