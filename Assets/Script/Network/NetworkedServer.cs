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
    string gameAccountFilePath;


    int playerLookingForMatch = -1;

    LinkedList<GameSession> gameSessionList;

    // Start is called before the first frame update
    void Start()
    {
        playerAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "PlayerAccountData.txt";
        gameAccountFilePath = Application.dataPath + Path.DirectorySeparatorChar + "GameAccountData.txt";


        NetworkTransport.Init();
        ConnectionConfig config = new ConnectionConfig();
        reliableChannelID = config.AddChannel(QosType.Reliable);
        unreliableChannelID = config.AddChannel(QosType.Unreliable);
        HostTopology topology = new HostTopology(config, maxConnections);
        hostID = NetworkTransport.AddHost(topology, socketPort, null);

        playerAccount = new LinkedList<PlayerAccount>();
        gameSessionList = new LinkedList<GameSession>();
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
        else if (signifier == ClientToServerSignifiers.StartLookingForPlayer)
        {
            if(playerLookingForMatch == -1)
            {
                playerLookingForMatch = id;
            }
            else
            {
                Debug.Log(findUniqueGameID());
                GameSession gs = new GameSession(playerLookingForMatch, id, findUniqueGameID());
                gameSessionList.AddLast(gs);
                //Player 2
                SendMessageToClient(ServerToClientSignifiers.GameSessionStarted + "", id);
                SendMessageToClient(ServerToClientSignifiers.GameResponses + "," + GameResponses.playerTwo, id);
                //Player 1
                SendMessageToClient(ServerToClientSignifiers.GameSessionStarted + "", playerLookingForMatch);
                SendMessageToClient(ServerToClientSignifiers.GameResponses + "," + GameResponses.playerOne, playerLookingForMatch);
              


                playerLookingForMatch = -1;
            }

        }
        else if (signifier == ClientToServerSignifiers.TicTacToeMove)
        {
            
            GameSession gs = FindGameSessionWithPlayersID(id);
            if(gs == null)
            {
                Debug.Log("No Game Session Found " + gameSessionList.Count);
                return;
            }
            int temp = 0;
            if(gs.playerID1 == id)
            {
                //Debug.Log(int.Parse(csv[2]));
                SendMessageToClient(ServerToClientSignifiers.OppnentTicTacToePlay + "," + int.Parse(csv[1]) + "," + int.Parse(csv[2]) + "," + GameResponses.playerOne, gs.playerID2);
                gs.currentGameRecord.turnAndMove.Add(csv[2]);
                temp = GameResponses.playerOne;
            }
            else if(gs.playerID2 == id)
            {
                //Debug.Log(int.Parse(csv[2]));
                SendMessageToClient(ServerToClientSignifiers.OppnentTicTacToePlay + "," + int.Parse(csv[1]) + "," + int.Parse(csv[2]) + "," + GameResponses.playerTwo, gs.playerID1);
                temp = GameResponses.playerTwo;
                gs.currentGameRecord.turnAndMove.Add(csv[2]);
            }

            if (gs.observerID.Count > 0)
            {
                foreach (int obsID in gs.observerID)
                {
                    SendMessageToClient(ServerToClientSignifiers.OppnentTicTacToePlay + "," + int.Parse(csv[1]) + "," + int.Parse(csv[2]) + "," + temp, obsID);
                }
            }

        }
        else if (signifier == ClientToServerSignifiers.messagingAnotherPlayer)
        {
            GameSession gs = FindGameSessionWithPlayersID(id);

            if (gs.playerID1 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.messagingAnotherPlayer + "," + csv[1], gs.playerID2);
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.messagingAnotherPlayer + "," + csv[1], gs.playerID1);
            }
        }
        else if (signifier == ClientToServerSignifiers.lookForGameToWatch)
        {
            GameSession gs = FindGameSessionWithPlayersID(int.Parse(csv[1]));
            if(gs != null)
            {
                SendMessageToClient(ServerToClientSignifiers.lookforGameResponses + "," + lookforGameResponses.Success, id);
                gs.addObserver(id);
            }
            else
            {
                SendMessageToClient(ServerToClientSignifiers.lookforGameResponses + "," + lookforGameResponses.Fail, id);
            }
        }
        else if (signifier == ClientToServerSignifiers.matchIsOver)
        {
            GameSession gs = FindGameSessionWithPlayersID(id);
            if (gs.playerID1 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.matchIsOver + "," + int.Parse(csv[1]), gs.playerID2);
            }
            else if (gs.playerID2 == id)
            {
                SendMessageToClient(ServerToClientSignifiers.matchIsOver + "," + int.Parse(csv[1]), gs.playerID1);
            }
            if (gs.observerID.Count > 0)
            {
                foreach (int obsID in gs.observerID)
                {
                    SendMessageToClient(ServerToClientSignifiers.matchIsOver + "," + int.Parse(csv[1]), obsID);
                }
            }
            SaveGame(gs);
            gameSessionList.Remove(gs);
        }
        else if (signifier == ClientToServerSignifiers.findReplay)
        {
            string gs = LoadGame(csv[1]);
            SendMessageToClient(ServerToClientSignifiers.sendReplay + "," + gs, id);
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
    private void SaveGame(GameSession currentGame)
    {
        StreamWriter sw = new StreamWriter(gameAccountFilePath, true);
        string saveData, saveDataTwo;

        //gameID,turn,move,turn2,move2...
        saveData = currentGame.currentGameRecord.gameID;
        saveDataTwo = "";
        for (int i = 0; i < currentGame.currentGameRecord.turnAndMove.Count; i++)
        {
            saveDataTwo = saveDataTwo + currentGame.currentGameRecord.turnAndMove[i] + ",";
        }
        sw.WriteLine(saveData + "," + saveDataTwo);
        sw.Close();
    }

    private int findUniqueGameID()
    {
        if (File.Exists(playerAccountFilePath))
        {
            StreamReader sr = new StreamReader(gameAccountFilePath);
            int lastLine = 0;
            string line;
            while ((line = sr.ReadLine()) != null)
            {
                lastLine++;
            }
            return (lastLine + 1);
        }
        return 1;
    }
    private string LoadGame(string ID)
    {
        //gameRecord temp;
        string returingString;
        returingString = "";
        if (File.Exists(playerAccountFilePath))
        {
            StreamReader sr = new StreamReader(gameAccountFilePath);

            string line;
            while ((line = sr.ReadLine()) != null)
            {
                string[] csv = line.Split(',');

                if(csv[0] == ID)
                {
                    //temp = new gameRecord(ID);
                    for (int i = 1; i < (csv.Length - 1); i++)
                    {
                        Debug.Log(csv[i]);
                        //temp.turnAndMove.Add(csv[i]);
                        returingString += csv[i].ToString() + ",";
                    }                    
                    return returingString;
                }                
            }
        }
        return null;
    }



    private GameSession FindGameSessionWithPlayersID(int id)
    {
        foreach (GameSession game in gameSessionList)
        {
            if(game.playerID1 == id || game.playerID2 == id)
            {
                return game;
            }
        }
        return null;
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

public class gameRecord
{
    public string gameID;
    public List<string> turnAndMove;
    public gameRecord(string GameID)
    {
        gameID = GameID;
        turnAndMove = new List<string>();
    }
}

public class GameSession
{
    public int playerID1, playerID2;
    public List<int> observerID;
    public gameRecord currentGameRecord;
    public GameSession(int ID1, int ID2, int gameID)
    {
        playerID1 = ID1;
        playerID2 = ID2;
        currentGameRecord = new gameRecord(gameID.ToString());

        observerID = new List<int>();
    }
    public void addObserver(int IDObs)
    {
        observerID.Add(IDObs);
    }
}

public static class ClientToServerSignifiers
{
    public const int Login = 1;

    public const int CreateAccount = 2;

    public const int StartLookingForPlayer = 3;

    public const int TicTacToeMove = 4;

    public const int messagingAnotherPlayer = 5;

    public const int lookForGameToWatch = 6;

    public const int matchIsOver = 7;

    public const int findReplay = 8;
}

public static class ServerToClientSignifiers
{
    public const int LoginResponses = 1;

    public const int GameSessionStarted = 2;

    public const int OppnentTicTacToePlay = 3;

    public const int GameResponses = 4;

    public const int messagingAnotherPlayer = 5;

    public const int lookforGameResponses = 6;

    public const int matchIsOver = 7;

    public const int sendReplay = 8;

}

public static class LoginResponses
{
    public const int Success = 1;

    public const int FailureNameInUse = 2;

    public const int FailureNameNotFound = 3;

    public const int FailureIncorrectPassword = 4;
}

public static class lookforGameResponses
{
    public const int Success = 1;

    public const int Fail = 2;

}


public static class GameResponses
{
    public const int observer = 0;
    
    public const int playerOne = 1;

    public const int playerTwo = 2;

}