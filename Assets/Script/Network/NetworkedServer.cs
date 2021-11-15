using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using System.IO;
using UnityEngine.UI;

public class NetworkedServer : MonoBehaviour
{
    int maxConnections = 1000;
    int reliableChannelID;
    int unreliableChannelID;
    int hostID;
    int socketPort = 5491;

    LinkedList<PlayerAccount> playerAccount;
    string playerAccountFilePath;

    // Start is called before the first frame update
    void Start()
    {
        playerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccountData.txt";

        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccount = new LinkedList<PlayerAccount>();
        LoadPlayerAccount();
    }

    // Update is called once per frame
    void Update()
    {

        int recHostID;
        int recConnectionID;
        int recChannelID;
        byte[] recBuffer = new byte[1024];
        int bufferSize = 1024;
        int dataSize;
        byte error = 0;

        NetworkEventType recNetworkEvent = NetworkTransport.Receive(out recHostID, out recConnectionID, out recChannelID, recBuffer, bufferSize, out dataSize, out error);

        switch (recNetworkEvent)
        {
            case NetworkEventType.Nothing:
                break;
            case NetworkEventType.ConnectEvent:
                Debug.Log("Connection, " + recConnectionID);
                break;
            case NetworkEventType.DataEvent:
                string msg = Encoding.Unicode.GetString(recBuffer, 0, dataSize);
                ProcessRecievedMsg(msg, recConnectionID);
                break;
            case NetworkEventType.DisconnectEvent:
                Debug.Log("Disconnection, " + recConnectionID);
                break;
        }

    }

    public void SendMessageToClient(string msg, int id)
    {
        byte error = 0;
        byte[] buffer = Encoding.Unicode.GetBytes(msg);
        NetworkTransport.Send(hostID, id, reliableChannelID, buffer, msg.Length * sizeof(char), out error);
    }

    private void ProcessRecievedMsg(string msg, int id)
    {
        Debug.Log("msg recieved = " + msg + ".  connection id = " + id);

        string[] csv = msg.Split(',');
        int signifier = int.Parse(csv[0]);

        if(signifier == ClientToServerSignifiers.CreateAccount)
        {
            string n = csv[1];
            string p = csv[2];
            bool isUnique = true;

            foreach(PlayerAccount acc in playerAccount)
            {
                if(acc.name == n)
                {
                    isUnique = false;
                    break;
                }
            }

            if (isUnique)
            {
                playerAccount.AddLast(new PlayerAccount(n, p));
                SendMessageToClient(ServerToClientSignifiers.LoginResponses + "," + LoginResponses.Success, id);

                SavePlayerAccount();
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponses + "," + LoginResponses.FailureNameInUse, id);
            }

        }
        else if(signifier == ClientToServerSignifiers.Login)
        {
            string n = csv[1];
            string p = csv[2];
            bool hasBeenFound = false;

            foreach (PlayerAccount acc in playerAccount)
            {
                if (acc.name == n)
                {
                    if(acc.password == p)
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponses + "," + LoginResponses.Success, id);
                    }
                    else
                    {
                        SendMessageToClient(ServerToClientSignifiers.LoginResponses + "," + LoginResponses.FailureIncorrectPassword, id);
                    }
                    hasBeenFound = true;
                    break;
                }
            }

            if (!hasBeenFound)
            {
                SendMessageToClient(ServerToClientSignifiers.LoginResponses + "," + LoginResponses.FailureNameNotFound, id);
            }

        }
    }

    private void SavePlayerAccount()
    {
        StreamWriter sw = new StreamWriter(playerAccountFilePath);

        foreach (PlayerAccount acc in playerAccount)
        {
            sw.WriteLine(acc.name + "," + acc.password);   
        }
        sw.Close();
    }

    private void LoadPlayerAccount()
    {
        if (File.Exists(playerAccountFilePath))
        {
            StreamReader sr = new StreamReader(playerAccountFilePath);

            string line;
            while((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                PlayerAccount pa = new PlayerAccount(csv[0], csv[1]);
                playerAccount.AddLast(pa);
            }
        }
    }
}

public class PlayerAccount
{
    public string name, password;
    
    public PlayerAccount(string Name, string Password)
    {
        name = Name;
        password = Password;
    }
}

public static class ClientToServerSignifiers
{
    public const int Login = 1;

    public const int CreateAccount = 2;
}

public static class ServerToClientSignifiers
{
    public const int LoginResponses = 1;
}

public static class LoginResponses
{
    public const int Success = 1;

    public const int FailureNameInUse = 2;

    public const int FailureNameNotFound = 3;

    public const int FailureIncorrectPassword = 4;
}
