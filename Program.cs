// See https://aka.ms/new-console-template for more information
using k8s;
using k8s.Models;
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
Console.WriteLine("Hello, World!");

var config = KubernetesClientConfiguration.BuildConfigFromConfigFile();
IKubernetes client = new Kubernetes(config);
Console.WriteLine("Starting port forward!");

var list = client.CoreV1.ListNamespacedPod("mongodb-trial");
var pod = list.Items[0];
await Forward(client, pod);


static async Task Forward(IKubernetes client, V1Pod pod)
{
    // Note this is single-threaded, it won't handle concurrent requests well...
    var webSocket = await client.WebSocketNamespacedPodPortForwardAsync(pod.Metadata.Name, "mongodb-trial", new int[] { 27017 }, "v4.channel.k8s.io");
    var demux = new StreamDemuxer(webSocket, StreamType.PortForward);
    demux.Start();

    var stream = demux.GetStream((byte?)0, (byte?)0);

    IPAddress ipAddress = IPAddress.Loopback;
    IPEndPoint localEndPoint = new IPEndPoint(ipAddress, 28016);
    Socket listener = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
    listener.Bind(localEndPoint);
    try
    {
        listener.Listen(100);
    } catch (Exception ex)
    {
        Console.WriteLine(ex.ToString());
    }
    

    Socket handler = null;

    // Note this will only accept a single connection
    var accept = Task.Run(() =>
    {
        while (true)
        {
            handler = listener.Accept();
            var bytes = new byte[4096];
            while (true)
            {
                int bytesRec = handler.Receive(bytes);
                stream.Write(bytes, 0, bytesRec);
                if (bytesRec == 0 || Encoding.ASCII.GetString(bytes, 0, bytesRec).IndexOf("<EOF>") > -1)
                {
                    break;
                }
            }
        }
    });

    var copy = Task.Run(() =>
    {
        var buff = new byte[4096];
        while (true)
        {
            var read = stream.Read(buff, 0, 4096);
            handler.Send(buff, read, 0);
        }
    });

    await accept;
    await copy;
    if (handler != null)
    {
        handler.Close();
    }
    listener.Close();
}
