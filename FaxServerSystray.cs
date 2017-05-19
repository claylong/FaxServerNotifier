using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;

/**********************************Simple Tray Icon sample DOTNET 2.0***********************************************
 * This class creates the notification icon that dotnet 2.0 offers.
 * It will be displaying the status of the application with appropiate icons.
 * It will have a contextmenu that enables the user to open the form or exit the application.
 * The form could be used to change settings of the app which in turn are saved in the app.config or some other file.
 * This formless, useless, notification sample does only chane the icon and balloontext.
 * NOTE:Chacker is a Singleton class so it will only allow to be instantiated once, and therefore only one instance.
 * I have done this to prevent more then one icon on the tray and to share data with the form (if any)
 *
 ******************************************************************************************************************/

namespace Fax_Server_Notifier
{
    class FaxServerSystray : IDisposable
    {
        //Checker is a singleton
        private static readonly FaxServerSystray notifier = new FaxServerSystray();

        //state the sample app could be in
        enum state
        {
            Idle,
            Connecting,
            Connected,
            Fax_Failed,
            Error
        }
        state iconstate = state.Idle;

        //fax server
        private FAXCOMEXLib.FaxServer objFaxServer;
        public string faxServerURI;
        private string lastMessageID;

        //timer
        bool IsDisposing = false;
        //int TimeOut = 10000; //10 sec 
        //System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();

        //notify icon: prepare the icons we may use in the notification
        NotifyIcon notify;
        System.Drawing.Icon iconIdle = new System.Drawing.Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("Fax_Server_Notifier.Resources.Alarm-Help-and-Support.ico"));
        System.Drawing.Icon iconConnecting = new System.Drawing.Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("Fax_Server_Notifier.Resources.Alarm-Synchonize.ico"));
        System.Drawing.Icon iconConnected = new System.Drawing.Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("Fax_Server_Notifier.Resources.Alarm-Tick.ico"));
        System.Drawing.Icon iconReceived_success = new System.Drawing.Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("Fax_Server_Notifier.Resources.Alarm-Info.ico"));
        System.Drawing.Icon iconReceived_failed = new System.Drawing.Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("Fax_Server_Notifier.Resources.Alarm-Warning.ico"));
        System.Drawing.Icon iconError = new System.Drawing.Icon(Assembly.GetExecutingAssembly().GetManifestResourceStream("Fax_Server_Notifier.Resources.Alarm-Error.ico"));

        //GUI: the form is not loaded into memory before it used, after use it is removed from memory
        //The notification icon has a contextmenu
        SettingsDialog formSettings;
        bool formPresent=false;
        ContextMenu contextmenu = new ContextMenu();

        /**************************** Singleton *****************************************************************
         * Make the constructor private and create a public method that returns the object reference.
         * The method must be static to be able to be called from different classes at any given moment.
         * The object is created when the first reference is ask for.
         * After that no more instances are created.
         ********************************************************************************************************/

        //public int Timeout
        //{
        //    set
        //    {
        //        this.TimeOut = value * 1000;
        //        this.timer.Interval = this.TimeOut;
        //    }
        //    get
        //    {
        //        return (this.TimeOut / 1000);
        //    }
        //}


        public static FaxServerSystray GetNotifierObject()
        {
            return notifier;
        }


        private FaxServerSystray() //singleton so private constructor!
        {
            //create menu
            //popup contextmenu when doubleclicked or when clicked in the menu.
            MenuItem item = new MenuItem("Open Windows Fax and Scan", new EventHandler(notify_DoubleClick));
            item.DefaultItem = true;
            contextmenu.MenuItems.Add(item);
            ////-----------
            //item = new MenuItem("-");
            //contextmenu.MenuItems.Add(item);
            //settings window
            item = new MenuItem("&Settings...", new EventHandler(Menu_Settings));
            contextmenu.MenuItems.Add(item);
            //add a exit submenu item
            item = new MenuItem("E&xit", new EventHandler(Menu_OnExit));
            contextmenu.MenuItems.Add(item);

            //notifyicon
            notify = new NotifyIcon();
            notify.ContextMenu = contextmenu;
            notify.DoubleClick += new EventHandler(notify_DoubleClick); //run default action when clicked
            notify.BalloonTipClicked += new EventHandler(notify_BalloonClick); //run most recent when clicked
            setState(state.Idle);
            notify.Visible = true;

            //load server settings from registry
            try
            {
                if (Application.UserAppDataRegistry.GetValue("FaxServer") != null)
                {
                    this.faxServerURI = Application.UserAppDataRegistry.GetValue("FaxServer").ToString();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
            connectToFaxServer();

            //timer for sample actions
            //timer.Tick += new EventHandler(timer_Tick);
            //timer.Interval = 10000;
            //timer.Start();
        }


        // New Incoming Message
        public void objFaxServer_OnIncomingMessageAdded(FAXCOMEXLib.FaxServer pFaxServer, string bstrMessageId)
        {
            //MessageBox.Show("There was a fax added to the incoming archive");
            this.lastMessageID = bstrMessageId;
            notify.BalloonTipText = "New message arrived!";
            notify.ShowBalloonTip(5000);
        }

        // Outgoing job changed
        public void objFaxServer_OnOutgoingJobChanged(FAXCOMEXLib.FaxServer pFaxServer, string bstrJobId, FAXCOMEXLib.FaxJobStatus pJobStatus)
        {
            //MessageBox.Show("There was a fax Changed to the outgoing queue");
            if (pJobStatus.Status == FAXCOMEXLib.FAX_JOB_STATUS_ENUM.fjsFAILED ||
                    pJobStatus.Status == FAXCOMEXLib.FAX_JOB_STATUS_ENUM.fjsNOLINE ||
                    pJobStatus.Status == FAXCOMEXLib.FAX_JOB_STATUS_ENUM.fjsRETRIES_EXCEEDED ||
                    pJobStatus.Status == FAXCOMEXLib.FAX_JOB_STATUS_ENUM.fjsCANCELED)
            {
                setState(state.Fax_Failed);
                notify.BalloonTipText = "An outgoing fax failed to be sent!";
                notify.ShowBalloonTip(10000);
            }
            else if (pJobStatus.Status == FAXCOMEXLib.FAX_JOB_STATUS_ENUM.fjsRETRYING)
            {
                notify.BalloonTipText = "Retrying send of an outgoing fax...";
                notify.ShowBalloonTip(2500);
            }
        }

        // Outgoing job added
        //public void objFaxServer_OnOutgoingJobAdded(FAXCOMEXLib.FaxServer pFaxServer, string bstrJobId)
        //{
        //    MessageBox.Show("There was a fax added to the outgoing queue");
        //}


        // Outgoing job removed
        //public void objFaxServer_OnOutgoingJobRemoved(FAXCOMEXLib.FaxServer pFaxServer, string bstrJobId)
        //{
        //    MessageBox.Show("Job Removed to outbound queue..");
        //}


        public void connectToFaxServer()
        {
            if (this.faxServerURI == null || this.faxServerURI == "")
            {
                //if the server url is blank, don't try to connect, go idle
                objFaxServer = null;
                setState(state.Idle);
            }
            else
            {
                //if the server url isn't blank, go ahead and try to connect
                try
                {
                    setState(state.Connecting);

                    //setup and connect
                    objFaxServer = new FAXCOMEXLib.FaxServer();
                    objFaxServer.Connect(this.faxServerURI);

                    //register for events
                    objFaxServer.ListenToServerEvents(
                        FAXCOMEXLib.FAX_SERVER_EVENTS_TYPE_ENUM.fsetFXSSVC_ENDED |
                        FAXCOMEXLib.FAX_SERVER_EVENTS_TYPE_ENUM.fsetIN_ARCHIVE |
                        FAXCOMEXLib.FAX_SERVER_EVENTS_TYPE_ENUM.fsetOUT_QUEUE);
                    objFaxServer.OnIncomingMessageAdded += new FAXCOMEXLib.IFaxServerNotify2_OnIncomingMessageAddedEventHandler(objFaxServer_OnIncomingMessageAdded);
                    //objFaxServer.OnOutgoingJobAdded += new FAXCOMEXLib.IFaxServerNotify2_OnOutgoingJobAddedEventHandler(objFaxServer_OnOutgoingJobAdded);
                    objFaxServer.OnOutgoingJobChanged += new FAXCOMEXLib.IFaxServerNotify2_OnOutgoingJobChangedEventHandler(objFaxServer_OnOutgoingJobChanged);
                    //objFaxServer.OnOutgoingJobRemoved += new FAXCOMEXLib.IFaxServerNotify2_OnOutgoingJobRemovedEventHandler(objFaxServer_OnOutgoingJobRemoved);

                    //if all goes well, set icon to connected
                    setState(state.Connected);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                    objFaxServer = null; 
                    setState(state.Error);
                }
            }
        }


        public void saveSettings()
        {
            //try to store the server in the registry
            try
            {
                Application.UserAppDataRegistry.SetValue("FaxServer", this.faxServerURI);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }


        //you could do some real checking here
        void setState(state toState)
        {
            //state fromState = iconstate;

            switch (toState)
            {
                case state.Idle:
                    {
                        notify.Icon = iconIdle;
                        notify.Text = "Not connected";
                        //notify.BalloonTipText = "Idle, not connected";
                        break;
                    }
                case state.Connecting:
                    {
                        notify.Icon = iconConnecting;
                        notify.Text = "Connecting to server...";
                        //notify.BalloonTipText = "Connecting to server...";
                        break;
                    }
                case state.Connected:
                    {
                        notify.Icon = iconConnected;
                        notify.Text = "Connected to " + this.faxServerURI;
                        notify.BalloonTipText = "Connected to " + this.faxServerURI + " Fax Server!";
                        //only show the balloon if this is a new connect
                        if (iconstate == state.Connecting)
                        {
                            notify.ShowBalloonTip(2500);
                        }
                        break;
                    }
                case state.Fax_Failed:
                    {
                        notify.Icon = iconReceived_failed;
                        notify.Text = "A fax failed to be sent or received";
                        break;
                    }
                default:
                    {
                        notify.Icon = iconError;
                        notify.Text = "Connection Error";
                        notify.BalloonTipText = "A problem occured while attempting to connect to the server";
                        notify.ShowBalloonTip(2500);
                        break;
                    }
            }

            iconstate = toState;

            //if (iconstate == state.Error)
            //    iconstate = state.Idle;
            //iconstate++;
        }


        void notify_BalloonClick(Object sender, EventArgs e)
        {
            //load last messageid, or ignore
            switch (iconstate)
            {
                case state.Connected:
                    {
                        // open last incoming message
                        openWFSMessage(this.lastMessageID);
                        break;
                    }
                case state.Fax_Failed:
                    {
                        // open outgoing folder
                        openWFSOutgoing();
                        // reset state
                        setState(state.Connected);
                        break;
                    }
                default:
                    {
                        // nothing!
                        break;
                    }
            }

        }

        void notify_DoubleClick(Object sender, EventArgs e)
        {
            //load default action
            switch (iconstate)
            {
                case state.Fax_Failed:
                    {
                        // open outgoing folder
                        openWFSOutgoing();
                        // reset state
                        setState(state.Connected);
                        break;
                    }
                default:
                    {
                        // open windows fax and scan
                        openWFS();
                        break;
                    }
            }
        }

        public void openWFSOutgoing()
        {
            // open ougoing folder in WFS
            Process wfs = new Process();
            wfs.StartInfo.FileName = "wfs.exe";
            wfs.StartInfo.Arguments = "/switch fax /folder outbox";
            wfs.Start();
        }

        public void openWFS()
        {
            // just open WFS
            Process wfs = new Process();
            wfs.StartInfo.FileName = "wfs.exe";
            wfs.StartInfo.Arguments = "/switch fax";
            wfs.Start();
        }

        public void openWFSMessage(string messageID)
        {
            // open a speicifc message in WFS
            Process wfs = new Process();
            wfs.StartInfo.FileName = "wfs.exe";
            wfs.StartInfo.Arguments = "/switch fax /MessageId" + messageID;
            wfs.Start();
        }

        //void Menu_ViewLatest(Object sender, EventArgs e)
        //{
        //    //open latest fax event

        //}


        void Menu_Settings(Object sender, EventArgs e)
        {
            //show the form for settings
            //prevent user from creating more then one form
            if (!formPresent)
            {
                formPresent = true;
                formSettings = new SettingsDialog();
                //use close event to reset formpresent boolean
                formSettings.FormClosed += new FormClosedEventHandler(form_FormClosed);
                formSettings.Show();
            }
        }


        void Menu_OnExit(Object sender, EventArgs e)
        {
            //be sure to call Application.Exit
            notify.Visible = false;
            Dispose();
            Application.Exit();
        }


        void form_FormClosed(object sender, FormClosedEventArgs e)
        {
            formPresent = false;
            formSettings = null;
        }


        ~FaxServerSystray()
        {
            Dispose();
        }


        public void Dispose()
        {
            if (!IsDisposing)
            {
                //timer.Stop();
                IsDisposing = true;
            }
        }
    }
}
