// Generated from tardigrade_interfaces/msg/ThrusterCommands.msg.
using System;
using Unity.Robotics.ROSTCPConnector.MessageGeneration;

namespace RosMessageTypes.TardigradeInterfaces
{
    [Serializable]
    public class ThrusterCommandsMsg : Message
    {
        public const string k_RosMessageName = "tardigrade_interfaces/ThrusterCommands";
        public override string RosMessageName => k_RosMessageName;

        public Std.HeaderMsg header;
        public string[] names;
        public float[] setpoints;

        public ThrusterCommandsMsg()
        {
            header = new Std.HeaderMsg();
            names = Array.Empty<string>();
            setpoints = Array.Empty<float>();
        }

        public ThrusterCommandsMsg(Std.HeaderMsg header, string[] names, float[] setpoints)
        {
            this.header = header;
            this.names = names;
            this.setpoints = setpoints;
        }

        public static ThrusterCommandsMsg Deserialize(MessageDeserializer deserializer) =>
            new ThrusterCommandsMsg(deserializer);

        ThrusterCommandsMsg(MessageDeserializer deserializer)
        {
            header = Std.HeaderMsg.Deserialize(deserializer);
            deserializer.Read(out names, deserializer.ReadLength());
            deserializer.Read(out setpoints, sizeof(float), deserializer.ReadLength());
        }

        public override void SerializeTo(MessageSerializer serializer)
        {
            serializer.Write(header);
            serializer.WriteLength(names);
            serializer.Write(names);
            serializer.WriteLength(setpoints);
            serializer.Write(setpoints);
        }

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#else
        [UnityEngine.RuntimeInitializeOnLoadMethod]
#endif
        public static void Register() => MessageRegistry.Register(k_RosMessageName, Deserialize);
    }
}
