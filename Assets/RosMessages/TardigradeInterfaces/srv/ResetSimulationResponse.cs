// Generated from tardigrade_interfaces/srv/ResetSimulation.srv.
using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.TardigradeInterfaces
{
    [Serializable]
    public class ResetSimulationResponse : Message
    {
        public const string k_RosMessageName = "tardigrade_interfaces/ResetSimulation";
        public override string RosMessageName => k_RosMessageName;

        public bool success;
        public string message;

        public ResetSimulationResponse()
        {
            success = false;
            message = "";
        }

        public ResetSimulationResponse(bool success, string message)
        {
            this.success = success;
            this.message = message;
        }

        public static ResetSimulationResponse Deserialize(MessageDeserializer deserializer) =>
            new ResetSimulationResponse(deserializer);

        ResetSimulationResponse(MessageDeserializer deserializer)
        {
            deserializer.Read(out success);
            deserializer.Read(out message);
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(success);
            serializer.Write(message);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register() => MessageRegistry.Register(
            k_RosMessageName, Deserialize, MessageSubtopic.Response);
    }
}
