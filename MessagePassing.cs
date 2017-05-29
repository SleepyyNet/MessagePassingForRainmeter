﻿using System;
using System.Runtime.InteropServices;
//using Newtonsoft.Json;
//using Newtonsoft.Json.Linq;
using WebSocketSharp;
using WebSocketSharp.Server;
using Rainmeter;
using System.Collections.Generic;

namespace MessagePassing
{
    //@TODO Write plugin to use multiple services and accept custom port configurations

    internal class Measure
    {
        //Used to keep track of skin ID's and the commands for open and close in a nicer manner
        //Note also takes the measure so that any skins that use dynamic variables do not create a new command on refresh
        class ExecuteCommand
        {
            public ExecuteCommand(IntPtr skin, string measure, string commandOnOpen, string commandOnClose)
            {
                this.Skin = skin;
                this.Measure = measure;
                this.CommandOnOpen = commandOnOpen;
                this.CommandOnClose = commandOnClose;
            }

            public IntPtr Skin { get; set; }
            public string Measure { get; set; }
            public string CommandOnOpen { get; set; }
            public string CommandOnClose { get; set; }
        }


        public static WebSocketServer wssv;
        public static string wsMessages = "";
        public static int instanceCount = 0;
        //private static string baseService = "/";

        //@TODO Combine all these lists?
        //List of current services, since services are kept alive this will only grow as more are declared
        private static List<string> services = new List<string>();

        //@TODO Add culling of skins that no longer exist (Or at least check if skin already exists so that dynamic variables can at least work)
        //A List of current services open and close command strings and the corresponding skin intptr in order of services
        private static List<List<ExecuteCommand>> commands = new List<List<ExecuteCommand>>();

        //Still keep a copy of the measures service for future bangs
        private string myService = "/";
        private int suppressErrors = 0;

        public class MessagePassing : WebSocketBehavior
        {
            protected override void OnMessage(MessageEventArgs e)
            {
                wsMessages = e.Data;
            }

            protected override void OnOpen()
            {
                base.OnOpen();
                //Count number of services
                int i = 0;
                var serviceClone = services;

                foreach (string service in services)
                {
                    if (service == this.Context.RequestUri.AbsolutePath)
                    {
                        //Foreach command in the same service
                        foreach (ExecuteCommand command in commands[i])
                        {
                            API.Execute(command.Skin, command.CommandOnOpen);
                        }
                    }
                    i++;
                }
            }
            protected override void OnClose(CloseEventArgs e)
            {
                base.OnClose(e);
                //Count number of services
                int i = 0;
                var serviceClone = services;

                foreach (string service in serviceClone)
                {
                    if (service == this.Context.RequestUri.AbsolutePath)
                    {
                        //Foreach command in the same service
                        foreach (ExecuteCommand command in commands[i])
                        {
                            API.Execute(command.Skin, command.CommandOnClose);
                        }
                    }
                    i++;
                }

            }
            //public void SendMessage(string stringToSend)
            //{
            //    Sessions.Broadcast(stringToSend);
            //}
        }



        internal Measure(Rainmeter.API api)
        {
            if (wssv == null)
            {
                wssv = new WebSocketServer(58932);
                //wssv.AddWebSocketService<MessagePassing>(baseService);
            }

            if (wssv.IsListening == false)
            {
                wssv.Start();
            }
        }

        internal virtual void Dispose()
        {

        }

        internal virtual void Reload(Rainmeter.API api, ref double maxValue)
        {
            //@TODO Use this port
            int port = api.ReadInt("Port", 58932);
            
            myService = api.ReadString("Name", "");
            //If my service does not start with a slash
            if(myService.Substring(0,1) != "/")
            {
                myService = "/" + myService;
            }

            string newCommandOnOpen = api.ReadString("OnOpen", "");
            string newCommandOnClose = api.ReadString("OnClose", "");
            IntPtr mySkin = api.GetSkin();
            string myMeasure = api.GetMeasureName();

            bool isNewService = true;
            int serviceLoc = 0;

            foreach(string service in services)
            {
                //If new service already exists
                if (myService == service)
                {
                    isNewService = false;
                    bool isNewSkin = true;

                    //Check if a skin with the same ID already exists if it does then just update instead of making a new skin
                    //Note: we just compare against onOpen, so make sure it is safe to assume they are always in the same loc from same skin @TODO Actually lets just merge execute command to have an open and a close command Edit: Mostly done
                    foreach(ExecuteCommand command in commands[serviceLoc])
                    {
                        //Check if from the same skin
                        if (command.Skin == mySkin)
                        {
                            //Check if from the same measure (No one really should be using two measures in the same service but since I did it for testing I might as well check for it as well) 
                            if (command.Measure == myMeasure)
                            {
                                isNewSkin = false;
                                command.CommandOnOpen = newCommandOnOpen;
                                command.CommandOnClose = newCommandOnClose;
                            }
                        }
                    }

                    if (isNewSkin)
                    {
                        commands[serviceLoc].Add(new ExecuteCommand(mySkin, myMeasure, newCommandOnOpen, newCommandOnClose));
                    }

                }
                serviceLoc++;
            }

            //If new service is actually new service
            if(isNewService)
            {
                services.Add(myService);

                //Add a new item in the top level (Services) of the list that contains a list of one command
                commands.Add(new List<ExecuteCommand>(new ExecuteCommand[] { new ExecuteCommand(mySkin, myMeasure, newCommandOnOpen, newCommandOnClose) } ));

                //Start new service
                wssv.AddWebSocketService<MessagePassing>(myService);
            }

            //@TODO Give errors as output if suppress errors is not on
            suppressErrors = api.ReadInt("SuppressErrors", 0);
        }

        internal void ExecuteBang(string args)
        {
            foreach (WebSocketServiceHost host in wssv.WebSocketServices.Hosts)
            {
                if (host.Path == myService)
                {
                    host.Sessions.Broadcast(args);
                }
                //Always broadcast to base case //TODO Decide if I want to keep this Edit: Decided due to adding the command and instancing protection to remove it
                //else if (host.Path == baseService)
                //{
                //    host.Sessions.Broadcast(args);
                //}
            }
        }

        internal virtual double Update()
        {

            return 0.0;
        }

        internal string GetString()
        {

            return wsMessages;
        }


    }
    public static class Plugin
    {
        static IntPtr StringBuffer = IntPtr.Zero;

        [DllExport]
        public static void Initialize(ref IntPtr data, IntPtr rm)
        {
            data = GCHandle.ToIntPtr(GCHandle.Alloc(new Measure(new Rainmeter.API(rm))));
            Measure.instanceCount++;
        }

        [DllExport]
        public static void Finalize(IntPtr data)
        {
            Measure.instanceCount--;
            if (Measure.instanceCount == 0)
            {
                Measure.wssv.Stop();
                Measure.wssv = null;
            }
            GCHandle.FromIntPtr(data).Free();

            if (StringBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }
        }

        [DllExport]
        public static void Reload(IntPtr data, IntPtr rm, ref double maxValue)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            measure.Reload(new Rainmeter.API(rm), ref maxValue);
        }

        [DllExport]
        public static double Update(IntPtr data)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            return measure.Update();
        }

        [DllExport]
        public static IntPtr GetString(IntPtr data)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            if (StringBuffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(StringBuffer);
                StringBuffer = IntPtr.Zero;
            }

            string stringValue = measure.GetString();
            if (stringValue != null)
            {
                StringBuffer = Marshal.StringToHGlobalUni(stringValue);
            }

            return StringBuffer;
        }
        [DllExport]
        public static void ExecuteBang(IntPtr data, IntPtr args)
        {
            Measure measure = (Measure)GCHandle.FromIntPtr(data).Target;
            measure.ExecuteBang(Marshal.PtrToStringUni(args));
        }
    }
}
