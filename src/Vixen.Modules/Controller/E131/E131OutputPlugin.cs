﻿//=====================================================================
//
//	OutputPlugin - E1.31 Plugin for Vixen 3.0
//
//		The original base code was generated by Visual Studio based
//		on the interface specification intrinsic to the Vixen plugin
//		technology. All other comments and code are the work of the
//		author. Some comments are based on the fundamental work
//		gleaned from published works by others in the Vixen community
//		including those of Jonathon Reinhart.
//
//=====================================================================

//=====================================================================
//
// Copyright (c) 2010 Joshua 1 Systems Inc. All rights reserved.
//
// Redistribution and use in source and binary forms, with or without modification, are
// permitted provided that the following conditions are met:
//
//    1. Redistributions of source code must retain the above copyright notice, this list of
//       conditions and the following disclaimer.
//
//    2. Redistributions in binary form must reproduce the above copyright notice, this list
//       of conditions and the following disclaimer in the documentation and/or other materials
//       provided with the distribution.
//
// THIS SOFTWARE IS PROVIDED BY JOSHUA 1 SYSTEMS INC. "AS IS" AND ANY EXPRESS OR IMPLIED
// WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND
// FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL <COPYRIGHT HOLDER> OR
// CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR
// CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR
// SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON
// ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING
// NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF
// ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
//
// The views and conclusions contained in the software and documentation are those of the
// authors and should not be interpreted as representing official policies, either expressed
// or implied, of Joshua 1 Systems Inc.
//
//=====================================================================

using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml;
using Common.Controls;
using Vixen.Commands;
using Vixen.Module.Controller;
using Vixen.Sys;
using Vixen.Sys.Output;
using VixenModules.Controller.E131.J1Sys;
using VixenModules.Output.E131;

namespace VixenModules.Controller.E131
{
	// -----------------------------------------------------------------
	// 
	// OutputPlugin - the output plugin class for vixen
	// 
	// -----------------------------------------------------------------

	public class E131OutputPlugin : ControllerModuleInstanceBase
	{
		internal static List<E131OutputPlugin> PluginInstances = new List<E131OutputPlugin>();
		internal static SortedList<string, int> unicasts = new SortedList<string, int>();
		private static bool _updateWarn = false;
		private static bool _missingInterfaceWarning = false;
		// our option settings

		private E131ModuleDataModel _data;
		private int _eventCnt;


		// a stringbuilder to store warnings, errors, and statistics
		private StringBuilder _messageTexts;

		// a sorted list of NetworkInterface object to use for sockets
		private SortedList<string, NetworkInterface> _nicTable;

		private long _totalTicks;
		private Stopwatch _updateStateStopWatch = new Stopwatch();

		private byte[] channelValues;

		internal bool isSetupOpen;

		/// <summary>
		///     Is the E1.31 controller currently running?
		/// </summary>
		private bool running = false;

		public E131OutputPlugin()
		{
			DataPolicyFactory = new DataPolicyFactory();
			isSetupOpen = false;
			running = false;
			SupportsNetwork = true;
		}

		public override Vixen.Module.IModuleDataModel ModuleData
		{
			get { return _data; }
			set { _data = (E131ModuleDataModel) value; }
		}


		public override bool HasSetup
		{
			get { return true; }
		}


		// -------------------------------------------------------------
		// 
		// 	Setup() - called when the user has requested to setup
		// 			  the plugin instance
		// 
		// -------------------------------------------------------------
		public override bool Setup()
		{
			isSetupOpen = true;

			using (var setupForm = new SetupForm())
			{
				// Tell the setupForm our output count
				setupForm.PluginChannelCount = this.OutputCount;

				List<int> initialUniverseList = new List<int>();

				setupForm.WarningsOption = _data.Warnings;
				setupForm.StatisticsOption = _data.Statistics;
				setupForm.EventRepeatCount = _data.EventRepeatCount;
				setupForm.EventSuppressCount = _data.EventSuppressCount;
				setupForm.AutoPopulateStart = _data.AutoPopulate;
				setupForm.Blind = _data.Blind;
				setupForm.Priority = _data.Priority;
				setupForm.SetDestination(_data.Multicast, _data.Unicast);

				// for each universe add it to setup form
				foreach (var uE in _data.Universes)
				{
					setupForm.UniverseAdd(
						uE.Active, uE.Universe, uE.Start + 1, uE.Size);
					initialUniverseList.Add(uE.Universe);
				}

				setupForm.Text = (new E131ModuleDescriptor()).TypeName + " Configuration - " +
				                 VixenSystem.OutputControllers.Single(
					                 controller => controller.ModuleInstanceId == _data.ModuleInstanceId).Name;

				if (setupForm.ShowDialog() == DialogResult.OK)
				{
					running = false; //prevent updates
					if (running)
					{
						this.Stop();
					}

					_data.Warnings = setupForm.WarningsOption;
					_data.Statistics = setupForm.StatisticsOption;
					_data.EventRepeatCount = setupForm.EventRepeatCount;
					_data.EventSuppressCount = setupForm.EventSuppressCount;
					_data.AutoPopulate = setupForm.AutoPopulateStart;
					_data.Blind = setupForm.Blind;
					_data.Priority = setupForm.Priority;
					_data.Universes.Clear();

					var destination = new Tuple<string, string>(null, null);

					destination = setupForm.GetDestination();

					_data.Unicast = destination.Item1;
					_data.Multicast = destination.Item2;

					OutputController thisController =
						VixenSystem.OutputControllers.Single(controller => controller.ModuleInstanceId == _data.ModuleInstanceId);

					for (int x = 0; x < thisController.Outputs.Length; x++)
						thisController.Outputs[x].Name = "Output #" + (x + 1).ToString();

					// add each of the universes as a child
					for (int i = 0; i < setupForm.UniverseCount; i++)
					{
						bool active = true;
						int universe = 0;
						int start = 0;
						int size = 0;

						if (setupForm.UniverseGet(
							i, ref active, ref universe, ref start, ref size))
						{
							_data.Universes.Add(new UniverseEntry(i, active, universe, start - 1, size, destination.Item1, destination.Item2));

							for (int x = start - 1; x < start + size - 1; x++)
								if (x < thisController.Outputs.Length)
									if (_data.Unicast == string.Empty || _data.Unicast == null)
										thisController.Outputs[x].Name = "#" + (x + 1).ToString() + " " + universe.ToString() + "-" +
										                                 (x - start + 2).ToString() + ": Multicast";
									else
										thisController.Outputs[x].Name = "#" + (x + 1).ToString() + " " + universe.ToString() + "-" +
										                                 (x - start + 2).ToString() + ": " + _data.Unicast.ToString();
						}
					}

					this.Start();
				}
			}

			isSetupOpen = false;

			return true;
		}

		// -------------------------------------------------------------
		// 
		// 	Stop() - called when execution is stopped or the
		// 				 plugin instance is no longer going to be
		// 				 referenced
		// 
		// -------------------------------------------------------------
		public override void Stop()
		{
			base.Stop();
			PluginInstances.Remove(this);
			running = false;

			//Close open sockets
			//If unicast then all universes are sharing a socket
			//so just close the first/only one.
			if (_data.Unicast != null)
			{
				if (_data.Universes != null && _data.Universes.Count > 0 && _data.Universes[0].Socket != null)
				{
					_data.Universes[0].Socket.Shutdown(SocketShutdown.Both);
					_data.Universes[0].Socket.Close();
					_data.Universes[0].Socket = null;
				}
			}
			else if (_data.Multicast != null)
			{
				// keep track of interface ids we have shutdown
				var idList = new SortedList<string, int>();

				// iterate through universetable
				foreach (var uE in _data.Universes)
				{
					// assume multicast
					string id = _data.Multicast;

					// if active
					if (uE.Active)
					{
						// and a usable socket
						if (uE.Socket != null)
						{
							// if not already done
							if (!idList.ContainsKey(id))
							{
								// record it & shut it down
								idList.Add(id, 1);
								uE.Socket.Shutdown(SocketShutdown.Both);
								uE.Socket.Close();
								uE.Socket = null;
							}
						}
					}
				}
			}

			if (_data.Statistics)
			{
				if (this._messageTexts.Length > 0)
				{
					this._messageTexts.AppendLine();
				}

				this._messageTexts.AppendLine(string.Format("Events: {0}", this._eventCnt));
				this._messageTexts.AppendLine(string.Format("Total Time: {0} Ticks; {1} ms", this._totalTicks,
					TimeSpan.FromTicks(this._totalTicks).Milliseconds));

				foreach (var uE in _data.Universes)
				{
					if (uE.Active)
					{
						this._messageTexts.AppendLine();
						this._messageTexts.Append(uE.StatsToText);
					}
				}

				J1MsgBox.ShowMsg(
					"Plugin Statistics:",
					this._messageTexts.ToString(),
					"J1Sys E1.31 Vixen Plugin",
					MessageBoxButtons.OK,
					MessageBoxIcon.Information);
			}

			// this._universeTable.Clear();
			if (this._nicTable != null) this._nicTable.Clear();
			this._nicTable = new SortedList<string, NetworkInterface>();
		}

		// -------------------------------------------------------------
		// 
		// 	Startup() - called when the plugin is loaded
		// 
		// 
		// 	todo:
		// 
		// 		1) probably add error checking on all 'new' operations
		// 		and system calls
		// 
		// 		2) better error reporting and logging
		//
		//      3) Sequence # should be per universe
		// 	
		// -------------------------------------------------------------
		public override void Start()
		{
			bool cleanStart = true;

			base.Start();

			if (!PluginInstances.Contains(this))
				PluginInstances.Add(this);

			// working copy of networkinterface object
			NetworkInterface networkInterface;

			// a single socket to use for unicast (if needed)
			Socket unicastSocket = null;

			// working ipaddress object
			IPAddress ipAddress = null;

			// a sortedlist containing the multicast sockets we've already done
			var nicSockets = new SortedList<string, Socket>();

			// load all of our xml into working objects
			this.LoadSetupNodeInfo();


			// initialize plugin wide stats
			this._eventCnt = 0;
			this._totalTicks = 0;

			if (_data.Unicast == null && _data.Multicast == null)
				if (_data.Universes[0] != null && (_data.Universes[0].Multicast != null || _data.Universes[0].Unicast != null))
				{
					_data.Unicast = _data.Universes[0].Unicast;
					_data.Multicast = _data.Universes[0].Multicast;
					if (!_updateWarn)
					{
						//messageBox Arguments are (Text, Title, No Button Visible, Cancel Button Visible)
						MessageBoxForm.msgIcon = SystemIcons.Information;
							//this is used if you want to add a system icon to the message form.
						var messageBox =
							new MessageBoxForm(
								"The E1.31 plugin is importing data from an older version of the plugin. Please verify the new Streaming ACN (E1.31) configuration.",
								"Vixen 3 Streaming ACN (E1.31) plugin", false, false);
						messageBox.ShowDialog();
						_updateWarn = true;
					}
				}

			// find all of the network interfaces & build a sorted list indexed by Id
			this._nicTable = new SortedList<string, NetworkInterface>();

			NetworkInterface[] nics = NetworkInterface.GetAllNetworkInterfaces();
			foreach (var nic in nics)
			{
				if (nic.NetworkInterfaceType != NetworkInterfaceType.Tunnel &&
				    nic.NetworkInterfaceType != NetworkInterfaceType.Loopback)
				{
					this._nicTable.Add(nic.Id, nic);
				}
			}

			if (_data.Unicast != null)
			{
				if (!unicasts.ContainsKey(_data.Unicast))
				{
					unicasts.Add(_data.Unicast, 0);
				}
			}

			// initialize messageTexts stringbuilder to hold all warnings/errors
			this._messageTexts = new StringBuilder();

			// now we need to scan the universeTable
			foreach (var uE in _data.Universes)
			{
				// if it's still active we'll look into making a socket for it
				if (cleanStart && uE.Active)
				{
					// if it's unicast it's fairly easy to do
					if (_data.Unicast != null)
					{
						// is this the first unicast universe?
						if (unicastSocket == null)
						{
							// yes - make a new socket to use for ALL unicasts
							unicastSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
						}

						// use the common unicastsocket
						uE.Socket = unicastSocket;

						IPAddress[] ips = null;

						try
						{
							ips = Dns.GetHostAddresses(_data.Unicast);
						}
						catch
						{
							//Probably couldn't find the host name
							NLog.LogManager.GetCurrentClassLogger().Warn("Couldn't connect to host " + _data.Unicast + ".");
							cleanStart = false;
						}

						if (ips != null)
						{
							IPAddress ip = null;
							foreach (IPAddress i in ips)
								if (i.AddressFamily == AddressFamily.InterNetwork)
									ip = i;

							// try to parse our ip address
							if (ip == null)
							{
								// oops - bad ip, fuss and deactivate
								NLog.LogManager.GetCurrentClassLogger().Warn("Couldn't connect to host " + _data.Unicast + ".");
								cleanStart = false;
								uE.Socket = null;
							}
							else
							{
								// if good, make our destination endpoint
								uE.DestIpEndPoint = new IPEndPoint(ip, 5568);
							}
						}
					}

					// if it's multicast roll up your sleeves we've got work to do
					else if (_data.Multicast != null)
					{
						// create an ipaddress object based on multicast universe ip rules
						var multicastIpAddress =
							new IPAddress(new byte[] {239, 255, (byte) (uE.Universe >> 8), (byte) (uE.Universe & 0xff)});

						// create an ipendpoint object based on multicast universe ip/port rules
						var multicastIpEndPoint = new IPEndPoint(multicastIpAddress, 5568);

						// first check for multicast id in nictable
						if (!this._nicTable.ContainsKey(_data.Multicast))
						{
							// no - deactivate and scream & yell!!
							NLog.LogManager.GetCurrentClassLogger()
								.Warn("Couldn't connect to use nic " + _data.Multicast + " for multicasting.");
							if (!_missingInterfaceWarning)
							{
								//messageBox Arguments are (Text, Title, No Button Visible, Cancel Button Visible)
								MessageBoxForm.msgIcon = SystemIcons.Warning;
									//this is used if you want to add a system icon to the message form.
								var messageBox =
									new MessageBoxForm(
										"The Streaming ACN (E1.31) plugin could not find one or more of the multicast interfaces specified. Please verify your network and plugin configuration.",
										"Vixen 3 Streaming ACN (E1.31) plugin", false, false);
								messageBox.ShowDialog();
								_missingInterfaceWarning = true;
							}
							cleanStart = false;
						}
						else
						{
							// yes - let's get a working networkinterface object
							networkInterface = this._nicTable[_data.Multicast];

							// have we done this multicast id before?
							if (nicSockets.ContainsKey(_data.Multicast))
							{
								// yes - easy to do - use existing socket
								uE.Socket = nicSockets[_data.Multicast];

								// setup destipendpoint based on multicast universe ip rules
								uE.DestIpEndPoint = multicastIpEndPoint;
							}
							// is the interface up?
							else if (networkInterface.OperationalStatus != OperationalStatus.Up)
							{
								// no - deactivate and scream & yell!!
								NLog.LogManager.GetCurrentClassLogger()
									.Warn("Nic " + _data.Multicast + " is available for multicasting bur currently down.");
								cleanStart = false;
							}
							else
							{
								// new interface in 'up' status - let's make a new udp socket
								uE.Socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

								// get a working copy of ipproperties
								IPInterfaceProperties ipProperties = networkInterface.GetIPProperties();

								// get a working copy of all unicasts
								UnicastIPAddressInformationCollection unicasts = ipProperties.UnicastAddresses;


								ipAddress = null;

								foreach (var unicast in unicasts)
								{
									if (unicast.Address.AddressFamily == AddressFamily.InterNetwork)
									{
										ipAddress = unicast.Address;
									}
								}

								if (ipAddress == null)
								{
									this._messageTexts.AppendLine(string.Format("No IP On Multicast Interface: {0} - {1}", networkInterface.Name,
										uE.InfoToText));
								}
								else
								{
									// set the multicastinterface option
									uE.Socket.SetSocketOption(
										SocketOptionLevel.IP,
										SocketOptionName.MulticastInterface,
										ipAddress.GetAddressBytes());

									// set the multicasttimetolive option
									uE.Socket.SetSocketOption(
										SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 64);

									// setup destipendpoint based on multicast universe ip rules
									uE.DestIpEndPoint = multicastIpEndPoint;

									// add this socket to the socket table for reuse
									nicSockets.Add(_data.Multicast, uE.Socket);
								}
							}
						}
					}
					else
					{
						NLog.LogManager.GetCurrentClassLogger()
							.Warn(
								"E1.31 plugin failed to start due to unassigned destinations. This can happen with newly created plugin instances that have yet to be configured.");
						cleanStart = false;
					}

					// if still active we need to create an empty packet
					if (cleanStart)
					{
						var zeroBfr = new byte[uE.Size];
						var e131Packet = new E131Packet(_data.ModuleInstanceId, "Vixen 3", 0, (ushort) uE.Universe, zeroBfr, 0, uE.Size,
							_data.Priority, _data.Blind);
						uE.PhyBuffer = e131Packet.PhyBuffer;
					}
				}
				if (cleanStart)
					running = true;
			}

			// any warnings/errors recorded?
			if (this._messageTexts.Length > 0)
			{
				// should we display them
				if (_data.Warnings)
				{
					// show our warnings/errors
					J1MsgBox.ShowMsg(
						"The following warnings and errors were detected during startup:",
						this._messageTexts.ToString(),
						"Startup Warnings/Errors",
						MessageBoxButtons.OK,
						MessageBoxIcon.Exclamation);

					// discard warning/errors after reporting them
					this._messageTexts = new StringBuilder();
				}
			}


#if VIXEN21
			return new List<Form> {};
#endif
		}

		#region Overrides of ControllerModuleInstanceBase

		/// <inheritdoc />
		public override ControllerNetworkConfiguration GetNetworkConfiguration()
		{
			ControllerNetworkConfiguration config = new ControllerNetworkConfiguration();
			config.SupportsUniverses = true;
			if (_data.Multicast == null)
			{
				config.TransmissionMethod = TransmissionMethods.Unicast;
				var ip = DetermineIp();
				config.IpAddress = ip != null ? ip.Address : IPAddress.Loopback;
				
			}
			else
				config.TransmissionMethod = TransmissionMethods.Multicast;

			List<UniverseConfiguration> universes = new List<UniverseConfiguration>(_data.Universes.Count);
			foreach (var universeEntry in _data.Universes)
			{
				var uc = new UniverseConfiguration();
				uc.UniverseNumber = universeEntry.Universe;
				uc.Start = universeEntry.Start+1;
				uc.Size = universeEntry.Size;
				uc.Active = universeEntry.Active;
				universes.Add(uc);
			}

			config.Universes = universes;
			return config;
		}

		#endregion

		private IPEndPoint DetermineIp()
		{
			IPEndPoint ep = null;
			IPAddress[] ips = null;
			try
			{
				ips = Dns.GetHostAddresses(_data.Unicast);
			}
			catch
			{
				//Probably couldn't find the host name
				NLog.LogManager.GetCurrentClassLogger().Warn("Couldn't lookup host ip " + _data.Unicast + ".");
			}

			if (ips != null)
			{
				// try to parse our ip address
				IPAddress ip = null;
				foreach (IPAddress i in ips)
				{
					if (i.AddressFamily == AddressFamily.InterNetwork)
					{
						ip = i;
					}
				}
					
				if (ip == null)
				{
					// oops - bad ip, fuss and deactivate
					NLog.LogManager.GetCurrentClassLogger().Warn("Couldn't connect to host " + _data.Unicast + ".");
				}
				else
				{
					// if good, make our destination endpoint
					ep = new IPEndPoint(ip, 5568);
				}
			}

			return ep;
		}

		private E131ModuleDataModel GetDataModel()
		{
			return (E131ModuleDataModel) this.ModuleData;
		}

		public override void UpdateState(int chainIndex, ICommand[] outputStates)
		{
			_updateStateStopWatch.Start();

			//Make sure the setup form is closed & the plugin has started
			if (isSetupOpen || !running)
			{
				return;
			}

			if (_data.Universes == null || _data.Universes.Count == 0)
			{
				return;
			}

			if (channelValues == null || channelValues.Length != outputStates.Length)
				channelValues = new byte[outputStates.Length];

			for (int index = 0; index < outputStates.Length; index++)
			{
				if (outputStates[index] is _8BitCommand command)
				{
					channelValues[index] = command.CommandValue;
				}
				else
				{
					// State reset
					channelValues[index] = 0;
				}
			}

			_eventCnt++;
			
			foreach (var uE in _data.Universes)
			{
				//Not sure why phybuf can be null, but the plugin will crash after being reconfigured otherwise.
				if (uE.PhyBuffer == null)
					continue;

				//Check if the universe is active and inside a valid channel range
				if (!uE.Active || uE.Start >= OutputCount || !running)
					continue;

				//Check the universe size boundary.
				int universeSize;
				if ((uE.Start + uE.Size) > OutputCount)
				{
					universeSize = OutputCount - uE.Start;
				}
				else
				{
					universeSize = uE.Size;
				}

				// Reduce duplicate packets... 
				// -the data. counts are the targets
				// -the uE. counts are how many have happened

				// do we want to suppress this one?  compare to last frame sent
				bool sendit = true;
				bool issame = _data.EventRepeatCount > 0 &&
				              E131Packet.CompareSlots(uE.PhyBuffer, channelValues, uE.Start, universeSize);
				if (issame)
				{
					// we allow the first event repeat count dups
					if (_data.EventRepeatCount > 0 && ++uE.EventRepeatCount >= _data.EventRepeatCount)
					{
						sendit = false;
						// we want to suppress, but should we force it anyway?
						if (_data.EventSuppressCount > 0 && ++uE.EventSuppressCount >= _data.EventSuppressCount)
						{
							sendit = true;
							uE.EventSuppressCount = 0;
						}
					}
				}
				else
				{
					// it's different so will go... clear counters
					// hopefully this happens within the 7.7 months it will take them to overflow :-)
					uE.EventRepeatCount = 0;
					uE.EventSuppressCount = 0;
				}

				if (sendit)
				{
					//SeqNumbers are per universe so that they can run independently
					E131Packet.CopySeqNumSlots(uE.PhyBuffer, channelValues, uE.Start, universeSize, uE.seqNum++);
					uE.Socket.SendTo(uE.PhyBuffer, uE.DestIpEndPoint);
					uE.PktCount++;
					uE.SlotCount += uE.Size;
				}
			}
			_updateStateStopWatch.Stop();

			this._totalTicks += _updateStateStopWatch.ElapsedTicks;
		}

		private void LoadSetupNodeInfo()
		{
			if (_data == null)
			{
				_data = new E131ModuleDataModel();
				_data.Universes = new List<UniverseEntry>();
				_data.Warnings = true;
				_data.Statistics = false;
				_data.EventRepeatCount = 0;
				_data.EventSuppressCount = 0;
				_data.AutoPopulate = true;
				_data.Blind = false;
				_data.Priority = 100;
			}

			if (_data.Universes == null)
				_data.Universes = new List<UniverseEntry>();

			if (System.IO.File.Exists("Modules\\Controller\\E131settings.xml"))
			{
				ImportOldSettingsFile();
				System.IO.File.Move("Modules\\Controller\\E131settings.xml", "Modules\\Controller\\E131settings.xml.old");
			}
		}

		private void ImportOldSettingsFile()
		{
			int rowNum = 1;

			//Setup the XML Document

			XmlNode _setupNode;
			XmlDocument doc;

			doc = new XmlDocument();

			doc.Load("Modules\\Controller\\E131settings.xml");

			//Navigate to the correct part of the XML file
			_setupNode = doc.ChildNodes.Item(1);


			foreach (XmlNode child in _setupNode.ChildNodes)
			{
				XmlAttributeCollection attributes = child.Attributes;
				XmlNode attribute;

				if (child.Name == "Options")
				{
					_data.Warnings = false;
					if ((attribute = attributes.GetNamedItem("warnings")) != null)
					{
						if (attribute.Value == "True")
						{
							_data.Warnings = true;
						}
					}

					_data.Statistics = false;
					if ((attribute = attributes.GetNamedItem("statistics")) != null)
					{
						if (attribute.Value == "True")
						{
							_data.Statistics = true;
						}
					}

					_data.EventRepeatCount = 0;
					if ((attribute = attributes.GetNamedItem("eventRepeatCount")) != null)
					{
						_data.EventRepeatCount = attribute.Value.TryParseInt32(0);
					}

					_data.EventSuppressCount = 0;
					if ((attribute = attributes.GetNamedItem("eventSuppressCount")) != null)
					{
						_data.EventSuppressCount = attribute.Value.TryParseInt32(0);
					}
				}

				if (child.Name == "Universe")
				{
					bool active = false;
					int universe = 1;
					int start = 1;
					int size = 1;
					string unicast = null;
					string multicast = null;
					int ttl = 1;

					if ((attribute = attributes.GetNamedItem("active")) != null)
					{
						if (attribute.Value == "True")
						{
							active = true;
						}
					}

					if ((attribute = attributes.GetNamedItem("number")) != null)
					{
						universe = attribute.Value.TryParseInt32(1);
					}

					if ((attribute = attributes.GetNamedItem("start")) != null)
					{
						start = attribute.Value.TryParseInt32(1);
					}

					if ((attribute = attributes.GetNamedItem("size")) != null)
					{
						size = attribute.Value.TryParseInt32(1);
					}

					if ((attribute = attributes.GetNamedItem("unicast")) != null)
					{
						unicast = attribute.Value;
					}

					if ((attribute = attributes.GetNamedItem("multicast")) != null)
					{
						multicast = attribute.Value;
					}

					if ((attribute = attributes.GetNamedItem("ttl")) != null)
					{
						ttl = attribute.Value.TryParseInt32(1);
					}

					_data.Universes.Add(
						new UniverseEntry(rowNum++, active, universe, start - 1, size, unicast, multicast, ttl));
				}
			}
		}
	}
}