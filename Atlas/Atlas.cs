﻿using System;
using System.Threading;
using System.Collections.Generic;
using Lidgren.Network;
using System.Linq;
using Edge.NetCommon;
using Microsoft.Xna.Framework;
using System.Threading.Tasks;

namespace Edge.Atlas {
	public partial class Atlas {
		NetServer server;
        bool runningHeadless;
        public bool isExiting;
		public Dictionary<long, DebugPlayer> players = new Dictionary<long, DebugPlayer>();
		public Dictionary<long, ServerEnemy> enemys = new Dictionary<long, ServerEnemy>();

		public long lastTime;
		public long currentTime = DateTime.UtcNow.Ticks;


		/// <summary>
		/// The entry point of the program, where the program control starts and ends.
		/// </summary>
		/// <param name="args">The command-line arguments.</param>
		public static void Main(string[] args) {
			if(args.Length > 0)
				try {
					new Atlas(int.Parse(args[0]), true).Run();
				}
				catch (Exception) {
					new Atlas(2348, true).Run();
				}
			else
				new Atlas(2348, true).Run();
		}

		/// <summary>
		/// Initializes a new instance of the <see cref="Edge.Atlas.Atlas"/> class.
		/// </summary>
		/// <param name="port">Port.</param>
		/// <param name="runningHeadless">If set to <c>false</c> do not bind to console</param>
		public Atlas(int port, bool runningHeadless) {
			this.runningHeadless = runningHeadless;

			var config = new NetPeerConfiguration("Atlas");
			config.EnableMessageType(NetIncomingMessageType.DiscoveryRequest);
			config.Port = port;
			server = new NetServer(config);
			server.Start();
		}

		/// <summary>
		/// Start the main server loop
		/// </summary>
		public void Run() {
			if(runningHeadless) {
				var inputThread = new Thread(InputHandler);
				inputThread.Start();                
			}
			long lastUpdates = 0;
			while(!isExiting) {
				lastTime = currentTime;
				currentTime = DateTime.UtcNow.Ticks;

				if(currentTime - lastUpdates <= (TimeSpan.TicksPerSecond / 120)) continue;
				#region Incoming messages
				NetIncomingMessage inMsg;
				while((inMsg = server.ReadMessage()) != null) {
					switch(inMsg.MessageType) {
						case NetIncomingMessageType.Data:
							 switch((AtlasPackets)inMsg.ReadByte()) {
                                 case AtlasPackets.RequestPositionChange:
                                    short X = inMsg.ReadInt16();
                                    short Y = inMsg.ReadInt16();
                                    byte R = inMsg.ReadByte();
                                    byte G = inMsg.ReadByte();
                                    byte B = inMsg.ReadByte();
                                    string Name = inMsg.ReadString();
                                    float Health = inMsg.ReadFloat();
                                    players[inMsg.SenderConnection.RemoteUniqueIdentifier].MoveVector = new Vector2(X, Y);
							        players[inMsg.SenderConnection.RemoteUniqueIdentifier].Name = Name;
                                    players[inMsg.SenderConnection.RemoteUniqueIdentifier].pColor = new Color(R,G,B);
                                    players[inMsg.SenderConnection.RemoteUniqueIdentifier].Health = Health;
                                    break;
							 }
							break;
						case NetIncomingMessageType.StatusChanged:
							switch(inMsg.SenderConnection.Status) {
								case NetConnectionStatus.Connected:
									players.Add(inMsg.SenderConnection.RemoteUniqueIdentifier, new DebugPlayer(inMsg.SenderConnection.RemoteUniqueIdentifier, 0, 0, 1));
                                    break;
								case NetConnectionStatus.Disconnected:
									players.Remove(inMsg.SenderConnection.RemoteUniqueIdentifier);
									break;
							}
							break;
						case NetIncomingMessageType.DiscoveryRequest:
							//TODO: FIX DIS
							NetOutgoingMessage response = server.CreateMessage();
							server.SendDiscoveryResponse(response, inMsg.SenderEndPoint);
							break;
						case NetIncomingMessageType.DebugMessage:
						case NetIncomingMessageType.VerboseDebugMessage:
						case NetIncomingMessageType.WarningMessage:
						case NetIncomingMessageType.ErrorMessage:
							Console.WriteLine(inMsg.ReadString());
							break;
					}
				}
				#endregion

				//Parallel.ForEach(players.Values, PlayerUpdate);
				foreach (var p in players)
					PlayerUpdate(p.Value);
                foreach (var e in enemys)
                    EnemyUpdate(e.Value);

				#region Outgoing Updates
				//TODO: Compute changed frames, keyframes, etc
				NetOutgoingMessage outMsg = server.CreateMessage();
				outMsg.Write((byte)AtlasPackets.UpdatePositions);
				outMsg.Write((ushort)players.Count);
				foreach (var p in players.Values) {
					outMsg.Write(p.NetID);
					outMsg.Write(p.Position.X);
					outMsg.Write(p.Position.Y);
                    outMsg.Write(p.pColor.R);
                    outMsg.Write(p.pColor.G);
                    outMsg.Write(p.pColor.B);
                    outMsg.Write(p.Name);
                    outMsg.Write(p.Health);
				}
				server.SendToAll(outMsg, NetDeliveryMethod.ReliableOrdered);

                outMsg = server.CreateMessage();
                outMsg.Write((byte)AtlasPackets.DamageEnemy);
                outMsg.Write((ushort)enemys.Count);
                foreach (var e in enemys.Values) {
                    outMsg.Write(e.NetID);
                    outMsg.Write(e.Position.X);
                    outMsg.Write(e.Position.Y);
                }
                server.SendToAll(outMsg, NetDeliveryMethod.ReliableOrdered);

                outMsg = server.CreateMessage();
                outMsg.Write((byte)AtlasPackets.UpdateMoveVector);
                outMsg.Write((ushort)players.Count);
			    foreach (var p in players.Values) {
			        outMsg.Write(p.MoveVector.X);
                    outMsg.Write(p.MoveVector.Y);
			    }
                server.SendToAll(outMsg, NetDeliveryMethod.ReliableOrdered);

				lastUpdates = currentTime;
				#endregion
			}
			server.Shutdown("Bye!");
		}

		/// <summary>
		/// Handles the input from the console
		/// </summary>
		void InputHandler() {
			while(!isExiting) {
				//Timing not needed, as ReadLine() locks the execution pointer
				string readLine = Console.ReadLine();
				#region Parse out the command and arguments
				//If they didn't say anything, we don't need to do anything
				if(String.IsNullOrWhiteSpace(readLine)) return;

				string command;
				#region Parse out parameterless commands
				try {
					//The command is anything that happens before the opening parenthesies
					command = readLine.Substring(0, readLine.IndexOf('('));
				}
				catch (Exception) {
					//If there isn't an opening parenthesies, it's a parameterless command
					command = readLine;
				}
				#endregion

				var args = new List<String>();
				//Take everything between the opening and closing parenthesies
				String argStr = readLine.Substring(readLine.IndexOf('(') + 1, readLine.IndexOf(')') - (readLine.IndexOf('(') + 1));
				try {
					//Try to split the arguments by commas
					args = argStr.Split(',').ToList();
				}
				catch (Exception) {
					try {
						//If the split failed, it ether had no arguments (do nothing)
						//or had only one argument (add that argument)
						if(!String.IsNullOrWhiteSpace(argStr))
							args.Add(argStr);
					}
					// Analysis disable once EmptyGeneralCatchClause
					catch {
					}
				}
				#endregion
				try {
					Control(command, args);
				}
				catch (NotSupportedException e) {
					Console.WriteLine(e.Message);
				}
			}
		}

		/// <summary>
		/// Control the server instance
		/// </summary>
		/// <param name="command">Command to be exected</param>
		/// <param name="args">Arguments being passed in</param>
		public void Control(String command, List<String> args) {
			switch(command.ToUpper()) {
				case "END":
				case "STOP":
				case "EXIT":
					isExiting = true;
					break;
				case "CLEAR":
				case "CLS":
					Console.Clear();
					break;
				case "ID":
					foreach (var p in players) {
						Console.WriteLine("ID: {0}\n\tPosition:({1},{2})\n\tMoving To:({3},{4})", p.Key, p.Value.Position.X, p.Value.Position.Y, p.Value.MovingTo.X, p.Value.MovingTo.Y);
					}
					break;
				case "ADDENT":
					if(args.Capacity > 0) {
						enemys.Add(long.Parse(args[0]), new ServerEnemy(long.Parse(args[0]), float.Parse(args[1]), float.Parse(args[2])));
                    }
                    break;
                case "ENTS":
                    foreach(var e in enemys)
                        Console.WriteLine("ID: {0}\n\tPosition:({1},{2})\n\tMoving To:({3},{4})");
                    break;
				case "PLAYERS":
                    foreach(var e in players)
                        Console.WriteLine("ID: {0}\n\tPosition:({1},{2})\n\tMoving To:({3},{4})");
                    break;
				case "MOVE": {
						var location = new Vector2(float.Parse(args[1]), float.Parse(args[2]));
						Console.WriteLine("moving to " + location);
						Int64 ID = Int64.Parse(args[0]);
						if(players.ContainsKey(ID))
							players[Convert.ToInt64(args[0])].MovingTo = location;
						break; 
					}
				default:
					{
						String argList = String.Empty;
						args.ForEach(arg => argList += arg + ",");
						throw new NotSupportedException(String.Format("Unrecognised command\nCommand: {0}\nArgs: {1}", command, argList));
					}
			}
		}
	}
}

