﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using NLog;
using System.Windows.Input;
using System.Windows;
using Tsunami.Core;

namespace Tsunami.ViewModel
{
    class TsunamiViewModel
    {
        //private static object _listLock = new object();
        //private static object _sessionLock = new object();
        private Logger log = LogManager.GetLogger("VM-Tsunami");

        private System.Timers.Timer _dispatcherTimer = new System.Timers.Timer();

        private ObservableCollection<Models.TorrentItem> _torrentList { get; set; }
        public ObservableCollection<Models.TorrentItem> TorrentList
        {
            get
            {
                //lock (_listLock)
                //{
                //}
                return _torrentList;
            }
        }

        private SessionStatistics _sessionStatistic { get; set; }
        public SessionStatistics SessionStatistic
        {
            get
            {
                return _sessionStatistic;
            }
        }

        private Models.Preferences _preference { get; set; }
        public Models.Preferences Preference
        {
            get
            {
                return _preference;
            }
        }

        private static Core.Session _torrentSession;

        public ICommand AddClick
        {
            get
            {
                return _addClick ?? (_addClick = new CommandHandler(() => AddClick_Dialog(), _addClickCanExecute));
            }
        }
        private ICommand _addClick;
        private bool _addClickCanExecute = true;

        Dictionary<Type, int> alertType = new Dictionary<Type, int>
        {
            {typeof(Core.torrent_added_alert),0},
            {typeof(Core.state_update_alert),1},
            {typeof(Core.torrent_paused_alert),2},
            {typeof(Core.torrent_resumed_alert),3},
            {typeof(Core.torrent_removed_alert),4},
            {typeof(Core.torrent_deleted_alert),5},
            {typeof(Core.torrent_error_alert),6},
            {typeof(Core.dht_stats_alert),7},
            {typeof(Core.dht_bootstrap_alert),8},
            {typeof(Core.save_resume_data_alert),9},
            {typeof(Core.save_resume_data_failed_alert),10},
            {typeof(Core.piece_finished_alert),11}
        };

        public TsunamiViewModel()
        {
            _torrentList = new ObservableCollection<Models.TorrentItem>();
            _sessionStatistic = new SessionStatistics();
            _torrentSession = new Core.Session();

            Settings.Logger.Inizialize();
            //Settings.User.readFromFile();
            _preference = new Models.Preferences();

            if (System.IO.File.Exists(".session_state"))
            {
                var data = System.IO.File.ReadAllBytes(".session_state");
                using (var entry = Core.Util.lazy_bdecode(data))
                {
                    _torrentSession.load_state(entry);
                }
            }

            _torrentSession.start_natpmp();
            _torrentSession.start_upnp();

            Core.DhtSettings dhts = _torrentSession.get_dht_settings();
            dhts.aggressive_lookups = true;
            _torrentSession.set_dht_settings(dhts);

            _torrentSession.start_dht();

            var alertMask = Core.AlertMask.error_notification
                            | Core.AlertMask.peer_notification
                            | Core.AlertMask.port_mapping_notification
                            | Core.AlertMask.storage_notification
                            | Core.AlertMask.tracker_notification
                            | Core.AlertMask.status_notification
                            | Core.AlertMask.ip_block_notification
                            | Core.AlertMask.progress_notification
                            | Core.AlertMask.stats_notification | Core.AlertMask.dht_notification
                            ;

            _torrentSession.set_alert_mask(alertMask);
            //_torrentSession.set_alert_dispatch(HandleAlertCallback);
            _torrentSession.Session_SetGetAlertCallback(HandlePendingAlertCallback);
            _torrentSession.Session_SetCallback(HandleAlertCallback);

            _dispatcherTimer.Elapsed += new System.Timers.ElapsedEventHandler(dispatcherTimer_Tick);
            _dispatcherTimer.Interval = Settings.Application.DISPATCHER_INTERVAL;
            _dispatcherTimer.Start();

            log.Debug("created");
        }

        private void dispatcherTimer_Tick(object sender, System.Timers.ElapsedEventArgs e)
        {
            //lock (_sessionLock)
            //{
                _torrentSession.post_torrent_updates();
                _torrentSession.post_dht_stats();
                //_torrentSession.post_session_stats();
            //}
        }

        private void HandleAlertCallback()
        {
            _torrentSession.Session_GetPendingAlert();
        }

        private void HandlePendingAlertCallback(Core.Alert a)
        {
            //using (a)
            //{
            log.Trace("libtorrent event {0}: {1}", a.what(), a.message());
            if (!alertType.ContainsKey(a.GetType()))
            {
                return;
            }
            switch (alertType[a.GetType()])
            {
                case 0:
                    TorrentAddedAlert((Core.torrent_added_alert)a);
                    break;
                case 1:
                    StateUpdateAlert((Core.state_update_alert)a);
                    break;
                case 2: // torrent_paused_alert
                    break;
                case 3: // torrent_resumed_alert
                    break;
                case 4: // torrent_removed_alert
                    break;
                case 5: // torrent_deleted_alert
                    break;
                case 6: // torrent_error_alert
                    break;
                case 7: // dht_stats_alert
                    break;
                case 8: // dht_bootstrap_alert
                    break;
                case 9: // save_resume_data_alert
                    break;
                case 10: // save_resume_data_failed_alert
                    break;
                case 11: // piece_finished_alert
                    break;
                default:
                    break;
            }
            //}
        }

        public void AddClick_Dialog()
        {
            Microsoft.Win32.OpenFileDialog ofd = new Microsoft.Win32.OpenFileDialog();
            ofd.Filter = "Torrent|*.torrent";
            ofd.Multiselect = true;
            ofd.CheckFileExists = true;
            ofd.CheckPathExists = true;
            ofd.Title = "Select Torrent to Add";
            ofd.ShowDialog();
            foreach (string file in ofd.FileNames)
            {
                AddTorrent(file);
            }
            Classes.Switcher.Switch(MainWindow._listPage);
        }

        public void AddTorrent(string filename)
        {
            var data = System.IO.File.ReadAllBytes(filename);
            string newfilePath;

            using (var atp = new Core.AddTorrentParams())
            using (var ti = new Core.TorrentInfo(filename))
            {
                atp.save_path = Settings.User.PathDownload;
                atp.ti = ti;
                atp.flags &= ~Core.ATPFlags.flag_auto_managed; // remove auto managed flag
                atp.flags &= ~Core.ATPFlags.flag_paused; // remove pause on added torrent
                atp.flags &= ~Core.ATPFlags.flag_use_resume_save_path; // 
                newfilePath = "./Fastresume/" + ti.info_hash().ToString() + ".torrent";
                if (!System.IO.File.Exists(newfilePath))
                {
                    // MANCA FAST RESUME DATA
                    //using (var bw = new System.IO.BinaryWriter(new System.IO.FileStream(newfilePath, System.IO.FileMode.Create)))
                    //{
                    //    bw.Write(data);
                    //    bw.Close();
                    //}
                }
                //lock (_sessionLock)
                //{
                    _torrentSession.async_add_torrent(atp);
                //}
            }
        }

        public void PauseTorrent(string hash)
        {
            //lock (_sessionLock)
            //{
                using (Core.Sha1Hash sha1hash = new Core.Sha1Hash(hash))
                using (Core.TorrentHandle th = _torrentSession.find_torrent(sha1hash))
                {
                    if (th != null  && th.is_valid())
                    {
                        th.pause();
                    }
                }
            //}
        }

        public void ResumeTorrent(string hash)
        {
            //lock (_sessionLock)
           // {
                using (Core.Sha1Hash sha1hash = new Core.Sha1Hash(hash))
                using (Core.TorrentHandle th = _torrentSession.find_torrent(sha1hash))
                {
                    if (th != null && th.is_valid())
                    {
                        th.resume();
                    }
                }
           // }
        }

        public void RemoveTorrent(string hash, bool deleteFileToo = false)
        {
            using (Core.Sha1Hash sha1hash = new Core.Sha1Hash(hash))
            using (Core.TorrentHandle th = _torrentSession.find_torrent(sha1hash))
            {
                if (th != null && th.is_valid())
                {
                    _torrentSession.remove_torrent(th, Convert.ToInt32(deleteFileToo));
                    if (TorrentList.Count(e => e.Hash == hash) > 0)
                    {
                        TorrentList.Remove(TorrentList.First(f => f.Hash == hash));
                    }
                }
            }
        }

        private void TorrentAddedAlert(Core.torrent_added_alert a)
        {
            using (Core.TorrentHandle th = a.handle)
            using (Core.TorrentStatus ts = th.status())
            using (Core.Sha1Hash hash = th.info_hash())
            {
                var stat = "Paused";
                if (!ts.paused)
                {
                    stat = Classes.Utils.GiveMeStateFromEnum(ts.state);
                }
                Models.TorrentItem ti = new Models.TorrentItem()
                {
                    Name = ts.name,
                    Hash = hash.ToString(),
                    Priority = ts.priority,
                    QueuePosition = ts.queue_position,
                    State = stat,
                    TotalWanted = ts.total_wanted,
                    TotalDone = ts.total_done,
                    Progress = ts.progress,
                    DownloadRate = ts.download_rate,
                    UploadRate = ts.upload_rate
                };
                //App.Current.Dispatcher.Invoke((Action)delegate
                //{
                //    TorrentList.Add(ti);
                //});
                System.Windows.Application.Current.Dispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Normal,
                    (Action)delegate ()
                    {
                        //lock (_listLock)
                        //{
                            TorrentList.Add(ti);
                        //}
                    });
            }
        }

        private void StateUpdateAlert(Core.state_update_alert a)
        {
           // lock (_sessionLock)
           // {
                Core.TorrentStatus[] statuses = (Core.TorrentStatus[])a.status.Clone();
                //foreach (Core.TorrentStatus ts in a.status)
                foreach (Core.TorrentStatus ts in statuses)
                using (ts)
                using (Core.Sha1Hash hash = ts.info_hash)
                {
                    var stat = "Paused";
                    if (!ts.paused)
                    {
                        stat = Classes.Utils.GiveMeStateFromEnum(ts.state);
                    }
                   // lock (_listLock)
                   // {
                        if (TorrentList.Count(z => z.Hash == hash.ToString()) == 1)
                        {
                            Models.TorrentItem ti = TorrentList.First(e => e.Hash == hash.ToString());
                            ti.DownloadRate = ts.download_rate;
                            ti.Priority = ts.priority;
                            ti.Progress = ts.progress;
                            ti.QueuePosition = ts.queue_position;
                            ti.State = stat;
                            ti.TotalDone = ts.total_done;
                            ti.UploadRate = ts.upload_rate; 
                        }
                   // }
                }
                using (Core.SessionStatus ss = _torrentSession.status())
                {
                    SessionStatistic.Update(ss);
                }
           // }
        }
    }

    public class CommandHandler : ICommand
    {
        private Action _action;
        private bool _canExecute;
        public CommandHandler(Action action, bool canExecute)
        {
            _action = action;
            _canExecute = canExecute;
        }

        public bool CanExecute(object parameter)
        {
            return _canExecute;
        }

        public event EventHandler CanExecuteChanged;

        public void Execute(object parameter)
        {
            _action();
        }
    }

}