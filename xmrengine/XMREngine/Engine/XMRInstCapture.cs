//////////////////////////////////////////////////////////////
//
// Copyright (c) 2009 Careminster Limited and Melanie Thielker
// Copyright (c) 2010 Mike Rieker, Beverly, MA, USA
//
// All rights reserved
//

using System;
using System.Threading;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Remoting.Lifetime;
using System.Security.Policy;
using System.IO;
using System.Xml;
using System.Text;
using Mono.Tasklets;
using OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.ScriptEngine.Interfaces;
using OpenSim.Region.ScriptEngine.Shared;
using OpenSim.Region.ScriptEngine.Shared.Api;
using OpenSim.Region.ScriptEngine.Shared.ScriptBase;
using OpenSim.Region.ScriptEngine.XMREngine;
using OpenSim.Region.Framework.Scenes;
using LSL_Float = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLFloat;
using LSL_Integer = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLInteger;
using LSL_Key = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_List = OpenSim.Region.ScriptEngine.Shared.LSL_Types.list;
using LSL_Rotation = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Quaternion;
using LSL_String = OpenSim.Region.ScriptEngine.Shared.LSL_Types.LSLString;
using LSL_Vector = OpenSim.Region.ScriptEngine.Shared.LSL_Types.Vector3;
using log4net;

namespace OpenSim.Region.ScriptEngine.XMREngine
{
    public partial class XMRInstance
    {
        /********************************************************************************\
         *  The only method of interest to outside this module is GetExecutionState()   *
         *  which captures the current state of the script into an XML document.        *
         *                                                                              *
         *  The rest of this module contains support routines for GetExecutionState().  *
        \********************************************************************************/

        /**
         * @brief Create an XML element that gives the current state of the script.
         *   <ScriptState Asset=m_AssetID>
         *     <Snapshot>stackdump</Snapshot>
         *     <Running>m_Running</Running>
         *     <DetectArray ...
         *     <EventQueue ...
         *     <Permissions ...
         *     <Plugins />
         *   </ScriptState>
         * Updates the .state file while we're at it.
         */
        public XmlElement GetExecutionState(XmlDocument doc)
        {
            XmlElement scriptStateN = doc.CreateElement("", "ScriptState", "");
            scriptStateN.SetAttribute("Asset", m_AssetID.ToString());

            /*
             * Make sure we aren't executing part of the script so it stays 
             * stable.  Setting suspendOnCheckRun tells CheckRun() to suspend
             * and return out so RunOne() will release the lock asap.
             */
            suspendOnCheckRunHold = true;
            lock (m_RunLock)
            {
                /*
                 * Get copy of script stack in relocateable form.
                 */
                MemoryStream snapshotStream = new MemoryStream();
                MigrateOutEventHandler(snapshotStream);
                Byte[] snapshotBytes = snapshotStream.ToArray();
                snapshotStream.Close();
                string snapshotString = Convert.ToBase64String(snapshotBytes);
                XmlElement snapshotN = doc.CreateElement("", "Snapshot", "");
                snapshotN.AppendChild(doc.CreateTextNode(snapshotString));
                scriptStateN.AppendChild(snapshotN);

                /*
                 * "Running" says whether or not we are accepting new events.
                 */
                XmlElement runningN = doc.CreateElement("", "Running", "");
                runningN.AppendChild(doc.CreateTextNode(m_Running.ToString()));
                scriptStateN.AppendChild(runningN);

                /*
                 * "DoGblInit" says whether or not default:state_entry() will init global vars.
                 */
                XmlElement doGblInitN = doc.CreateElement("", "DoGblInit", "");
                doGblInitN.AppendChild(doc.CreateTextNode(doGblInit.ToString()));
                scriptStateN.AppendChild(doGblInitN);

                /*
                 * More misc data.
                 */
                XmlNode permissionsN = doc.CreateElement("", "Permissions", "");
                scriptStateN.AppendChild(permissionsN);

                XmlAttribute granterA = doc.CreateAttribute("", "granter", "");
                granterA.Value = m_Item.PermsGranter.ToString();
                permissionsN.Attributes.Append(granterA);

                XmlAttribute maskA = doc.CreateAttribute("", "mask", "");
                maskA.Value = m_Item.PermsMask.ToString();
                permissionsN.Attributes.Append(maskA);

                /*
                 * "DetectParams" are returned by llDetected...() script functions
                 * for the currently active event, if any.
                 */
                if (m_DetectParams != null)
                {
                    XmlElement detParArrayN = doc.CreateElement("", "DetectArray", "");
                    AppendXMLDetectArray(doc, detParArrayN, m_DetectParams);
                    scriptStateN.AppendChild(detParArrayN);
                }

                /*
                 * Save any events we have in the queue.
                 * <EventQueue>
                 *   <Event Name="...">
                 *     <param>...</param> ...
                 *     <DetectParams>...</DetectParams> ...
                 *   </Event>
                 *   ...
                 * </EventQueue>
                 */
                XmlElement queuedEventsN = doc.CreateElement("", "EventQueue", "");
                lock (m_QueueLock)
                {
                    foreach (EventParams evt in m_EventQueue)
                    {
                        XmlElement singleEventN = doc.CreateElement("", "Event", "");
                        singleEventN.SetAttribute("Name", evt.EventName);
                        AppendXMLObjectArray(doc, singleEventN, evt.Params, "param");
                        AppendXMLDetectArray(doc, singleEventN, evt.DetectParams);
                        queuedEventsN.AppendChild(singleEventN);
                    }
                }
                scriptStateN.AppendChild(queuedEventsN);

                /*
                 * "Plugins" indicate enabled timers and listens, etc.
                 */
                Object[] pluginData = 
                        AsyncCommandManager.GetSerializationData(m_Engine,
                                m_ItemID);

                XmlNode plugins = doc.CreateElement("", "Plugins", "");
                AppendXMLObjectArray(doc, plugins, pluginData, "plugin");
                scriptStateN.AppendChild(plugins);

                /*
                 * Let script run again.
                 */
                suspendOnCheckRunHold = false;
            }

            /*
             * scriptStateN represents the contents of the .state file so
             * write the .state file while we are here.
             */
            FileStream fs = File.Create(m_StateFileName);
            StreamWriter sw = new StreamWriter(fs);
            sw.Write(scriptStateN.OuterXml);
            sw.Close();
            fs.Close();

            return scriptStateN;
        }

        /**
         * @brief Write script state to output stream.
         *
         * Input:
         *  stream = stream to write event handler state information to
         */
        private void MigrateOutEventHandler (Stream stream)
        {
            /*
             * The script microthread should be at its Suspend() call within
             * CheckRun(), unless it has exited.  Tell CheckRun() that it 
             * should migrate the script out then suspend.
             */
            this.migrateInReader  = null;
            this.migrateInStream  = null;
            this.migrateOutWriter = new BinaryWriter (stream);
            this.migrateOutStream = stream;

            /*
             * Write the basic information out to the stream:
             *    state, event, eventhandler args, script's globals.
             */
            stream.WriteByte (migrationVersion);
            this.SendObjValue (stream, this.stateCode);
            this.SendObjValue (stream, this.eventCode);
            this.SendObjValue (stream, this.heapLimit - this.heapLeft);
            this.SendObjValue (stream, this.ehArgs);
            this.MigrateScriptOut (stream, this.SendObjValue);

            /*
             * Resume the microthread to actually write the network stream.
             * When it finishes it will suspend, causing the microthreading
             * to return here.
             *
             * There is a stack only if the event code is not None.
             */
            if (this.eventCode != ScriptEventCode.None) {
                this.migrateComplete = false;
                Exception except = this.microthread.ResumeEx (null);
                if (except != null) {
                    throw except;
                }
                if (!this.migrateComplete) throw new Exception ("migrate out did not complete");
            }

            /*
             * No longer migrating.
             */
            this.migrateOutWriter = null;
            this.migrateOutStream = null;
        }

        /**
         * @brief Write script global variables to the output stream.
         */
        private void MigrateScriptOut (System.IO.Stream stream, MMRContSendObj sendObj)
        {
            SendObjArray (stream, sendObj, this.gblArrays);
            SendObjArray (stream, sendObj, this.gblFloats);
            SendObjArray (stream, sendObj, this.gblIntegers);
            SendObjArray (stream, sendObj, this.gblLists);
            SendObjArray (stream, sendObj, this.gblRotations);
            SendObjArray (stream, sendObj, this.gblStrings);
            SendObjArray (stream, sendObj, this.gblVectors);
        }

        private void SendObjArray (System.IO.Stream stream, MMRContSendObj sendObj, Array array)
        {
            sendObj (stream, (object)(int)array.Length);
            for (int i = 0; i < array.Length; i ++) {
                sendObj (stream, array.GetValue (i));
            }
        }

        /**
         * @brief called by continuation.Save() for every object to
         *        be sent over the network.
         * @param stream = network stream to send it over
         * @param graph = object to send
         */
        private void SendObjValue (Stream stream, object graph)
        {
            if (graph == null) {
                this.migrateOutWriter.Write ((byte)Ser.NULL);
            } else if (graph is ScriptEventCode) {
                this.migrateOutWriter.Write ((byte)Ser.EVENTCODE);
                this.migrateOutWriter.Write ((int)graph);
            } else if (graph is LSL_Float) {
                this.migrateOutWriter.Write ((byte)Ser.LSLFLOAT);
                this.migrateOutWriter.Write ((float)((LSL_Float)graph).value);
            } else if (graph is LSL_Integer) {
                this.migrateOutWriter.Write ((byte)Ser.LSLINT);
                this.migrateOutWriter.Write ((int)((LSL_Integer)graph).value);
            } else if (graph is LSL_Key) {
                this.migrateOutWriter.Write ((byte)Ser.LSLKEY);
                LSL_Key key = (LSL_Key)graph;
                SendObjValue (stream, key.m_string);  // m_string can be null
            } else if (graph is LSL_List) {
                this.migrateOutWriter.Write ((byte)Ser.LSLLIST);
                LSL_List list = (LSL_List)graph;
                SendObjValue (stream, list.Data);
            } else if (graph is LSL_Rotation) {
                this.migrateOutWriter.Write ((byte)Ser.LSLROT);
                this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).x);
                this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).y);
                this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).z);
                this.migrateOutWriter.Write ((double)((LSL_Rotation)graph).s);
            } else if (graph is LSL_String) {
                this.migrateOutWriter.Write ((byte)Ser.LSLSTR);
                LSL_String str = (LSL_String)graph;
                SendObjValue (stream, str.m_string);  // m_string can be null
            } else if (graph is LSL_Vector) {
                this.migrateOutWriter.Write ((byte)Ser.LSLVEC);
                this.migrateOutWriter.Write ((double)((LSL_Vector)graph).x);
                this.migrateOutWriter.Write ((double)((LSL_Vector)graph).y);
                this.migrateOutWriter.Write ((double)((LSL_Vector)graph).z);
            } else if (graph is XMR_Array) {
                this.migrateOutWriter.Write ((byte)Ser.XMRARRAY);
                ((XMR_Array)graph).SendArrayObj (this.SendObjValue, stream);
            } else if (graph is object[]) {
                this.migrateOutWriter.Write ((byte)Ser.OBJARRAY);
                object[] array = (object[])graph;
                int len = array.Length;
                this.migrateOutWriter.Write (len);
                for (int i = 0; i < len; i ++) {
                    SendObjValue (stream, array[i]);
                }
            } else if (graph is double) {
                this.migrateOutWriter.Write ((byte)Ser.SYSDOUB);
                this.migrateOutWriter.Write ((double)graph);
            } else if (graph is float) {
                this.migrateOutWriter.Write ((byte)Ser.SYSFLOAT);
                this.migrateOutWriter.Write ((float)graph);
            } else if (graph is int) {
                this.migrateOutWriter.Write ((byte)Ser.SYSINT);
                this.migrateOutWriter.Write ((int)graph);
            } else if (graph is string) {
                this.migrateOutWriter.Write ((byte)Ser.SYSSTR);
                this.migrateOutWriter.Write ((string)graph);
            } else {
                throw new Exception ("unhandled class " + graph.GetType().ToString());
            }
        }

        /**
         * @brief Convert an DetectParams[] to corresponding XML.
         *        DetectParams[] holds the values retrievable by llDetected...() for
         *        a given event.
         */
        private static void AppendXMLDetectArray(XmlDocument doc, XmlElement parent, DetectParams[] detect)
        {
            foreach (DetectParams d in detect)
            {
                XmlElement detectParamsN = GetXMLDetect(doc, d);
                parent.AppendChild(detectParamsN);
            }
        }

        private static XmlElement GetXMLDetect(XmlDocument doc, DetectParams d)
        {
            XmlElement detectParamsN = doc.CreateElement("", "DetectParams", "");
            XmlAttribute pos = doc.CreateAttribute("", "pos", "");
            pos.Value = d.OffsetPos.ToString();
            detectParamsN.Attributes.Append(pos);

            XmlAttribute d_linkNum = doc.CreateAttribute("",
                    "linkNum", "");
            d_linkNum.Value = d.LinkNum.ToString();
            detectParamsN.Attributes.Append(d_linkNum);

            XmlAttribute d_group = doc.CreateAttribute("",
                    "group", "");
            d_group.Value = d.Group.ToString();
            detectParamsN.Attributes.Append(d_group);

            XmlAttribute d_name = doc.CreateAttribute("",
                    "name", "");
            d_name.Value = d.Name.ToString();
            detectParamsN.Attributes.Append(d_name);

            XmlAttribute d_owner = doc.CreateAttribute("",
                    "owner", "");
            d_owner.Value = d.Owner.ToString();
            detectParamsN.Attributes.Append(d_owner);

            XmlAttribute d_position = doc.CreateAttribute("",
                    "position", "");
            d_position.Value = d.Position.ToString();
            detectParamsN.Attributes.Append(d_position);

            XmlAttribute d_rotation = doc.CreateAttribute("",
                    "rotation", "");
            d_rotation.Value = d.Rotation.ToString();
            detectParamsN.Attributes.Append(d_rotation);

            XmlAttribute d_type = doc.CreateAttribute("",
                    "type", "");
            d_type.Value = d.Type.ToString();
            detectParamsN.Attributes.Append(d_type);

            XmlAttribute d_velocity = doc.CreateAttribute("",
                    "velocity", "");
            d_velocity.Value = d.Velocity.ToString();
            detectParamsN.Attributes.Append(d_velocity);

            detectParamsN.AppendChild(
                doc.CreateTextNode(d.Key.ToString()));

            return detectParamsN;
        }

        /**
         * @brief Append elements of an array of objects to an XML parent.
         * @param doc = document the parent is part of
         * @param parent = parent to append the items to
         * @param array = array of objects
         * @param tag = <tag ..>...</tag> for each element
         */
        private static void AppendXMLObjectArray(XmlDocument doc, XmlNode parent, object[] array, string tag)
        {
            foreach (object o in array)
            {
                XmlElement element = GetXMLObject(doc, o, tag);
                parent.AppendChild(element);
            }
        }

        /**
         * @brief Get and XML representation of an object.
         * @param doc = document the tag will be put in
         * @param o = object to be represented
         * @param tag = <tag ...>...</tag>
         */
        private static XmlElement GetXMLObject(XmlDocument doc, object o, string tag)
        {
            XmlAttribute typ = doc.CreateAttribute("", "type", "");
            XmlElement n = doc.CreateElement("", tag, "");

            if (o is LSL_List)
            {
                typ.Value = "list";
                n.Attributes.Append(typ);
                AppendXMLObjectArray(doc, n, ((LSL_List)o).Data, "item");
            }
            else
            {
                typ.Value = o.GetType().ToString();
                n.Attributes.Append(typ);
                n.AppendChild(doc.CreateTextNode(o.ToString()));
            }
            return n;
        }
    }
}
