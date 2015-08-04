﻿/*
 * Copyright 2015 Mikhail Shiryaev
 * 
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 * 
 *     http://www.apache.org/licenses/LICENSE-2.0
 * 
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 * 
 * Product  : Rapid SCADA
 * Module   : ScadaCommCommon
 * Summary  : UDP communication channel logic
 * 
 * Author   : Mikhail Shiryaev
 * Created  : 2015
 * Modified : 2015
 */

using Scada.Comm.Devices;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;

namespace Scada.Comm.Channels
{
    /// <summary>
    /// UDP communication channel logic
    /// <para>Логика работы канала связи UDP</para>
    /// </summary>
    public class CommUdpLogic : CommChannelLogic
    {
        /// <summary>
        /// Режимы выбора КП для обработки входящих запросов
        /// </summary>
        public enum DeviceSelectionModes
        {
            /// <summary>
            /// По IP-адресу
            /// </summary>
            ByIPAddress,
            /// <summary>
            /// Используя библиотеку КП
            /// </summary>
            ByDeviceLibrary
        }

        /// <summary>
        /// Настройки канала связи
        /// </summary>
        public class Settings
        {
            /// <summary>
            /// Конструктор
            /// </summary>
            public Settings()
            {
                // установка значений по умолчанию
                LocalUdpPort = 0;
                RemoteUdpPort = 0;
                RemoteIpAddress = "";
                Behavior = CommChannelLogic.OperatingBehaviors.Master;
                DevSelMode = CommUdpLogic.DeviceSelectionModes.ByIPAddress;
            }

            /// <summary>
            /// Получить или установить локальный UDP-порт
            /// </summary>
            public int LocalUdpPort { get; set; }
            /// <summary>
            /// Получить или установить удалённый UDP-порт
            /// </summary>
            public int RemoteUdpPort { get; set; }
            /// <summary>
            /// Получить или установить удалённый IP-адрес по умолчанию
            /// </summary>
            public string RemoteIpAddress { get; set; }
            /// <summary>
            /// Получить или установить режим работы канала связи
            /// </summary>
            public OperatingBehaviors Behavior { get; set; }
            /// <summary>
            /// Получить или установить режим выбора КП
            /// </summary>
            public DeviceSelectionModes DevSelMode { get; set; }
        }

        /// <summary>
        /// Настройки канала связи
        /// </summary>
        protected Settings settings;
        /// <summary>
        /// UPD-соединение
        /// </summary>
        protected UdpConnection udpConn;
        /// <summary>
        /// Словарь КП по позывным
        /// </summary>
        protected Dictionary<string, KPLogic> kpCallNumDict;


        /// <summary>
        /// Конструктор
        /// </summary>
        public CommUdpLogic()
            : base()
        {
            settings = new Settings();
            udpConn = null;
            kpCallNumDict = new Dictionary<string, KPLogic>();
        }


        /// <summary>
        /// Получить наименование канала связи
        /// </summary>
        public override string InternalName
        {
            get
            {
                return "CommUdp";
            }
        }

        /// <summary>
        /// Получить режим работы
        /// </summary>
        public override OperatingBehaviors Behavior
        {
            get
            {
                return settings.Behavior;
            }
        }


        /// <summary>
        /// Запустить приём данных по UDP
        /// </summary>
        protected void StartUdpReceive()
        {
            udpConn.UdpClient.BeginReceive(new AsyncCallback(UdpReceiveCallback), null);
        }

        /// <summary>
        /// Обработать принятые по UDP данные
        /// </summary>
        protected void UdpReceiveCallback(IAsyncResult ar)
        {
            // приём данных
            IPEndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
            byte[] buf = udpConn.UdpClient.EndReceive(ar, ref remoteEP);
            udpConn.RemoteAddress = remoteEP.Address.ToString();
            udpConn.RemotePort = remoteEP.Port;
            WriteToLog(string.Format(Localization.UseRussian ? "{0} Получены данные от {1}:{2}" :
                "{0} Data received from {1}:{2}", CommUtils.GetNowDT(), udpConn.RemoteAddress, udpConn.RemotePort));

            if (buf == null)
            {
                WriteToLog(Localization.UseRussian ? "Данные пусты" : "Data is empty");
            }
            else if (kpListNotEmpty)
            {
                if (settings.DevSelMode == DeviceSelectionModes.ByIPAddress)
                {
                    KPLogic kpLogic;
                    if (kpCallNumDict.TryGetValue(udpConn.RemoteAddress, out kpLogic))
                    {
                        // обработка входящего запроса для определённого КП
                        ExecProcIncomingReq(kpLogic, buf, 0, buf.Length, ref kpLogic);
                    }
                    else
                    {
                        WriteToLog(string.Format(Localization.UseRussian ?
                            "{0} Не удалось найти КП по IP-адресу {1}" :
                            "{0} Unable to find device by IP address {1}",
                            CommUtils.GetNowDT(), udpConn.RemoteAddress));
                    }
                }
                else // DeviceSelectionModes.ByDeviceLibrary
                {
                    // обработка входящего запроса для произвольного КП
                    KPLogic targetKP = null;
                    ExecProcIncomingReq(firstKP, buf, 0, buf.Length, ref targetKP);
                }
            }

            StartUdpReceive();
        }


        /// <summary>
        /// Инициализировать канал связи
        /// </summary>
        public override void Init(SortedList<string, string> commCnlParams, List<KPLogic> kpList)
        {
            // вызов метода базового класса
            base.Init(commCnlParams, kpList);

            // получение настроек канала связи
            settings.LocalUdpPort = GetIntParam(commCnlParams, "LocalUdpPort", true, settings.LocalUdpPort);
            settings.RemoteUdpPort = GetIntParam(commCnlParams, "RemoteUdpPort", false, settings.RemoteUdpPort);
            settings.RemoteIpAddress = GetStringParam(commCnlParams, "RemoteIpAddress", 
                false, settings.RemoteIpAddress);
            settings.Behavior = GetEnumParam<OperatingBehaviors>(commCnlParams, "Behavior", 
                false, settings.Behavior);
            settings.DevSelMode = GetEnumParam<DeviceSelectionModes>(commCnlParams, "DevSelMode", 
                false, settings.DevSelMode);

            // создание клиента и соединения
            UdpClient udpClient = new UdpClient(settings.LocalUdpPort);
            udpConn = new UdpConnection(udpClient, settings.LocalUdpPort, settings.RemoteUdpPort);

            foreach (KPLogic kpLogic in kpList)
            {
                // добавление КП в словарь по позывным
                string callNum = kpLogic.CallNum;
                if (!string.IsNullOrEmpty(callNum) && !kpCallNumDict.ContainsKey(callNum))
                    kpCallNumDict.Add(callNum, kpLogic);

                // установка соединения всем КП на линии связи
                kpLogic.Connection = udpConn;
            }

            // проверка библиотек КП в режиме ведомого
            string warnMsg;
            if (settings.Behavior == OperatingBehaviors.Slave && !AreDllsEqual(out warnMsg))
                WriteToLog(warnMsg);
        }

        /// <summary>
        /// Запустить работу канала связи
        /// </summary>
        public override void Start()
        {
            if (settings.Behavior == OperatingBehaviors.Slave)
            {
                WriteToLog(string.Format(Localization.UseRussian ?
                    "{0} Запуск приёма данных по UDP на порту {1}" :
                    "{0} Start receiving data via UDP on port {1}", CommUtils.GetNowDT(), settings.LocalUdpPort));
                StartUdpReceive();
            }
        }

        /// <summary>
        /// Остановить работу канала связи
        /// </summary>
        public override void Stop()
        {
            // очистка ссылки на соединение для всех КП на линии связи
            foreach (KPLogic kpLogic in kpList)
                kpLogic.Connection = null;

            // очистка словаря КП по позывным
            kpCallNumDict.Clear();

            // закрытие соединения
            udpConn.Close();
            WriteToLog("");
            WriteToLog(string.Format(Localization.UseRussian ?
                "{0} Завершение приёма данных по UDP" :
                "{0} Stop receiving data via UDP", CommUtils.GetNowDT()));
        }

        /// <summary>
        /// Выполнить действия перед сеансом опроса КП или отправкой команды
        /// </summary>
        public override void BeforeSession(KPLogic kpLogic)
        {
            if (udpConn != null)
                udpConn.RemoteAddress = string.IsNullOrEmpty(kpLogic.CallNum) ? 
                    settings.RemoteIpAddress : kpLogic.CallNum;
        }
    }
}
