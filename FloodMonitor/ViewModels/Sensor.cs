﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Windows.Input;
using LiveCharts;
using LiveCharts.Configurations;

namespace FloodMonitor.ViewModels
{
    class Sensor:ModelBase<Sensor>
    {
       
        private static ObservableCollection<Sensor> _AllSensors;
        
        private static ListCollectionView _AllView;

        public static ListCollectionView AllView
        {
            get
            {
                if (_AllView != null) return _AllView;
                _AllView = (ListCollectionView) CollectionViewSource.GetDefaultView(Cache);
                _AllView.LiveFilteringProperties.Add(nameof(IsDeleted));
                _AllView.IsLiveFiltering = true;
                _AllView.SortDescriptions.Add(new SortDescription(nameof(Order), ListSortDirection.Ascending));
                _AllView.LiveSortingProperties.Add(nameof(Order));
                _AllView.Filter = o => !((Sensor) o).IsDeleted;

                Task.Factory.StartNew(async () =>
                {
                    while (true)
                    {
                        foreach (var sensor in Cache)
                        {
                            sensor.OnPropertyChanged(nameof(LastHeartBeatText));
                        }
                        await Task.Delay(7777);    
                    }
                });
                return _AllView;
            }
        }

        protected override string GetErrorInfo(string prop)
        {
            if (prop == nameof(Number))
            {
                if (string.IsNullOrEmpty(Number)) return "REQUIRED";
                if (!Number.IsCellNumber()) return "INVALID NUMBER";
            } else if (prop == nameof(SensorName) && string.IsNullOrEmpty(SensorName))
                return "REQUIRED";

            return base.GetErrorInfo(prop);
        }

        public override string ToString()
        {
            return Number;
        }

        private static ICommand _AddCommand;
        public static ICommand AddCommand => _AddCommand ?? (_AddCommand = new DelegateCommand(async d =>
        {
            var sensor = await NewSensorDialog.Show();
            if (sensor == null) return;

            var oldSensor = Cache.FirstOrDefault(x => x.Equals(sensor));
            if (oldSensor != null)
            {
                if (oldSensor.IsDeleted)
                {
                    oldSensor.Undelete();
                    oldSensor.SensorName = sensor.SensorName;
                    oldSensor.Location = sensor.Location;
                    oldSensor.Save();
                }
                return;
            }

            sensor.Save();
        },d=>MainViewModel.Instance.CurrentUser?.IsAdmin??false));

        private string _Number;
        public string Number
        {
            get => _Number;
            set
            {
                if (value == _Number) return;
                _Number = value;
                OnPropertyChanged(nameof(Number));
            }
        }

        private int _WarningLevel;

        public int WarningLevel
        {
            get => _WarningLevel;
            set
            {
                if (value == _WarningLevel) return;
                _WarningLevel = value;
                if (_WarningLevel > 5) _WarningLevel = 5;
                if (_WarningLevel < 0) _WarningLevel = 0;

                OnPropertyChanged(nameof(WarningLevel));
                OnPropertyChanged(nameof(IsWarning));

                if (Id > 0)
                {
                    SettingsSaved = false;
                    SettingsSent = false;
                    Save();
                }
            }
        }

        private long _Order = 0;

        public long Order
        {
            get => _Order>0?_Order:Id;
            set
            {
                if (value == _Order) return;
                _Order = value;
                OnPropertyChanged(nameof(Order));
                if (Id > 0)
                {
                    SettingsSaved = false;
                    SettingsSent = false;
                }
            }
        }
        
        public bool IsWarning => WarningLevel>0 && WaterLevel >= WarningLevel;
        
        private int _WaterLevel;
        public int WaterLevel
        {
            get => _WaterLevel;
            set
            {
                if (value == _WaterLevel) return;
                _WaterLevel = value;
                OnPropertyChanged(nameof(WaterLevel));
                OnPropertyChanged(nameof(IsWarning));
                if (Id>0 && IsWarning)
                {
                    MainViewModel.Instance.Notify($"{SensorName.ToUpper()} HAS REACHED LEVEL {WaterLevel}!");                    
                }
            }
        }

        public bool NumberEquals(string number)
        {
            if (number.Length < 2) return false;
            var n1 = number.StartsWith("+") ? number.Substring(3) : number.Substring(1);
            var n2 = Number.StartsWith("+") ? Number.Substring(3) : Number.Substring(1);
            return n1 == n2;
        }

        public override async void Delete()
        {
            if(await MessageDialog.Show("Are you sure you want to delete this sensor?","","_YES","_CANCEL"))
                base.Delete();
        }

        private ICommand _EditCommand;
        [Ignore]
        public ICommand EditCommand => _EditCommand ?? (_EditCommand = new DelegateCommand(async d =>
        {
            var sensor = await NewSensorDialog.Show(this);
            sensor?.Save();
        },d=>Id>0));

        public override bool Equals(object obj)
        {
            if (!(obj is Sensor s)) return false;
            return NumberEquals(s.Number);
        }

        private DateTime _LastHeartBeat;
        public DateTime LastHeartBeat
        {
            get => _LastHeartBeat;
            set
            {
                if (value == _LastHeartBeat) return;
                _LastHeartBeat = value;
                OnPropertyChanged(nameof(LastHeartBeat));
            }
        }

        public Sensor()
        {
            var mapper = Mappers.Xy<WaterLevel>().X(x => x.Id).Y(x => x.Level);
            Charting.For<WaterLevel>(mapper);
            LabelFormatter = v =>
            {
                if ((ViewModels.WaterLevel.Cache.FirstOrDefault()?.Id??0) == (long) v) return "";
                var level = ViewModels.WaterLevel.Cache.FirstOrDefault(x => x.Id == (long) v);
                return level?.DateTime.ToString("MMM d h:mm tt");
            };
        }

        private ChartValues<WaterLevel> _waterLevels;
        
        public ChartValues<WaterLevel> WaterLevels
        {
            get
            {
                if (_waterLevels != null) return _waterLevels;
                _waterLevels = new ChartValues<WaterLevel>();
                var levels = ViewModels.WaterLevel.Cache
                    .Where(x => x.SensorId == Id).ToList();
                var latest = levels.OrderByDescending(x=>x.Id).Take(47).ToList();
                    
                _waterLevels.AddRange(latest);
                LatestLevel = _waterLevels?.LastOrDefault();
                if (_waterLevels.Count == 0)
                {
                    SetLevel(0);
                    SetLevel(0);
                }
                return _waterLevels;
            }
        }

        public void ResetLevels()
        {
            _waterLevels = null;
            OnPropertyChanged(nameof(WaterLevels));
        }

        [Ignore]
        public Func<double, string> LabelFormatter { get; set; }
        
        private string _LastHeartBeatText;
        [Ignore]
        public string LastHeartBeatText
        {
            get
            {
                if (LastHeartBeat == DateTime.MinValue) return "NEVER";
                var elapsed = DateTime.Now - LastHeartBeat;
                if (elapsed.TotalMinutes < 2) return $"{(long)elapsed.TotalSeconds} seconds";
                if (elapsed.TotalHours < 2) return $"{(long)elapsed.TotalMinutes} minutes";
                if (elapsed.TotalDays < 2) return $"{(long) elapsed.TotalHours} hours";
                return $"{(long)elapsed.TotalDays} days ago";
            }
            set
            {
                if (value == _LastHeartBeatText) return;
                _LastHeartBeatText = value;
                OnPropertyChanged(nameof(LastHeartBeatText));
            }
        }
        
        private string _SensorName;
        public string SensorName
        {
            get => _SensorName;
            set
            {
                if (value == _SensorName) return;
                _SensorName = value;
                if (value.Length > 47 - 1)
                    _SensorName = value.Substring(0, 47 - 1);
                if (Id > 0)
                {
                    SettingsSaved = false;
                    SettingsSent = false;
                }
                _SensorName = _SensorName.Replace(",", "");
                OnPropertyChanged(nameof(SensorName));
            }
        }

        private int _Siren1 = 1;

        public int Siren1
        {
            get => _Siren1;
            set
            {
                if (value == _Siren1) return;
                _Siren1 = value;
                if (Id > 0)
                {
                    SettingsSaved = false;
                    SettingsSent = false;
                }
                if (value < 0) _Siren1 = 0;
                if (value > 5) _Siren1 = 5;
                OnPropertyChanged(nameof(Siren1));
            }
        }

        private int _Siren2 = 2;

        public int Siren2
        {
            get => _Siren2;
            set
            {
                if (value == _Siren2) return;
                _Siren2 = value;
                if (Id > 0)
                {
                    SettingsSaved = false;
                    SettingsSent = false;
                }
                if (value < 0) _Siren2 = 0;
                if (value > 5) _Siren2 = 5;
                OnPropertyChanged(nameof(Siren2));
            }
        }

        private int _Siren3 = 3;

        public int Siren3
        {
            get => _Siren3;
            set
            {
                if (value == _Siren3) return;
                _Siren3 = value;
                if (Id > 0)
                {
                    SettingsSaved = false;
                    SettingsSent = false;
                }
                if (value < 0) _Siren3 = 0;
                if (value > 5) _Siren3 = 5;
                OnPropertyChanged(nameof(Siren3));
            }
        }

        private bool _SettingsSaved;

        public bool SettingsSaved
        {
            get => _SettingsSaved;
            set
            {
                if (value == _SettingsSaved) return;
                _SettingsSaved = value;
                OnPropertyChanged(nameof(SettingsSaved));
            }
        }

        private bool _SettingsSent;
        [Ignore]
        public bool SettingsSent
        {
            get => _SettingsSent;
            set
            {
                if (value == _SettingsSent) return;
                _SettingsSent = value;
                OnPropertyChanged(nameof(SettingsSent));
            }
        }
        
        private string _Location;
        public string Location
        {
            get => _Location;
            set
            {
                if (value == _Location) return;
                _Location = value;
                if (value.Length > 74 - 1)
                    _SensorName = value.Substring(0, 74 - 1);
                if (Id > 0)
                {
                    SettingsSaved = false;
                    SettingsSent = false;
                }
                _SensorName = _SensorName.Replace(",", "");
                OnPropertyChanged(nameof(Location));
            }
        }
        
        protected override bool GetIsEmpty()
        {
            return false;
        }

        private bool _Verified;

        public bool Verified
        {
            get => _Verified;
            set
            {
                if (value == _Verified) return;
                _Verified = value;
                OnPropertyChanged(nameof(Verified));
            }
        }

        internal override void OnSaved()
        {
            if (Modem.Instance.IsOnline)
            {
                if (!Verified)
                    Modem.Instance.SendMessage(Number, "777\rhttps://goo.gl/RBy5eb");
                if (!SettingsSaved)
                {
                    Modem.Instance.SendMessage(Number,
                        $"={SensorName};{Siren1};{Siren2};{Siren3};{WarningLevel};\rhttps://goo.gl/RBy5eb");
                    SendSensors();
                    SettingsSent = true;
                }
            }

            base.OnSaved();
        }

        private string _Level1Message;

        public string Level1Message
        {
            get => _Level1Message;
            set
            {
                if (value == _Level1Message) return;
                _Level1Message = value;
                OnPropertyChanged(nameof(Level1Message));
            }
        }

        private string _Level2Message;

        public string Level2Message
        {
            get => _Level2Message;
            set
            {
                if (value == _Level2Message) return;
                _Level2Message = value;
                OnPropertyChanged(nameof(Level2Message));
            }
        }

        private string _Level3Message;

        public string Level3Message
        {
            get => _Level3Message;
            set
            {
                if (value == _Level3Message) return;
                _Level3Message = value;
                OnPropertyChanged(nameof(Level3Message));
            }
        }

        private string _Level4Message;

        public string Level4Message
        {
            get => _Level4Message;
            set
            {
                if (value == _Level4Message) return;
                _Level4Message = value;
                OnPropertyChanged(nameof(Level4Message));
            }
        }

        private string _Level5Message;

        public string Level5Message
        {
            get => _Level5Message;
            set
            {
                if (value == _Level5Message) return;
                _Level5Message = value;
                OnPropertyChanged(nameof(Level5Message));
            }
        }

        private string _WarningMessage;

        public string WarningMessage
        {
            get => _WarningMessage;
            set
            {
                if (value == _WarningMessage) return;
                _WarningMessage = value;
                OnPropertyChanged(nameof(WarningMessage));
            }
        }

        private bool _NotifyLevel1;

        public bool NotifyLevel1
        {
            get => _NotifyLevel1;
            set
            {
                if (value == _NotifyLevel1) return;
                _NotifyLevel1 = value;
                OnPropertyChanged(nameof(NotifyLevel1));
            }
        }

        private bool _NotifyLevel2;

        public bool NotifyLevel2
        {
            get => _NotifyLevel2;
            set
            {
                if (value == _NotifyLevel2) return;
                _NotifyLevel2 = value;
                OnPropertyChanged(nameof(NotifyLevel2));
            }
        }

        private bool _NotifyLevel3;

        public bool NotifyLevel3
        {
            get => _NotifyLevel3;
            set
            {
                if (value == _NotifyLevel3) return;
                _NotifyLevel3 = value;
                OnPropertyChanged(nameof(NotifyLevel3));
            }
        }

        private bool _NotifyLevel4;

        public bool NotifyLevel4
        {
            get => _NotifyLevel4;
            set
            {
                if (value == _NotifyLevel4) return;
                _NotifyLevel4 = value;
                OnPropertyChanged(nameof(NotifyLevel4));
            }
        }

        private bool _NotifyLevel5;

        public bool NotifyLevel5
        {
            get => _NotifyLevel5;
            set
            {
                if (value == _NotifyLevel5) return;
                _NotifyLevel5 = value;
                OnPropertyChanged(nameof(NotifyLevel5));
            }
        }
        
        public void SendSensors()
        {
            var sensors = Cache.Where(x => x.Order > Order).Take(7).ToList();
            Modem.Instance.SendMessage(Number,
                sensors.Count == 0
                    ? "!!\rhttps://goo.gl/RBy5eb"
                    : $"!{string.Join(";", sensors)};\rhttps://goo.gl/RBy5eb");
        }

        private WaterLevel _LatestLevel;
        [Ignore]
        public WaterLevel LatestLevel
        {
            get => _LatestLevel;
            set
            {
                if (value == _LatestLevel) return;
                _LatestLevel = value;
                OnPropertyChanged(nameof(LatestLevel));
            }
        }
        
        public void SetLevel(int level)
        {

            try
            {
                awooo.Context.Post(d=>{
                    var newLevel = new WaterLevel()
                    {
                        Level = level,
                        SensorId = Id
                    };
                    newLevel.Save();
                    WaterLevel = level;
                    LastHeartBeat = DateTime.Now;
                    Save();
                    LatestLevel = newLevel;
                    WaterLevels.Add(newLevel);
                },null);
                var msg = Config.GetMessage(level);
                if (string.IsNullOrEmpty(msg)) return;
                foreach (var monitor in Monitor.Cache)
                {
                    if(!monitor.Number.IsCellNumber()) continue;
                    Modem.Instance.SendMessage(monitor.Number,msg);
                }
            }
            catch (Exception e)
            {
                //
            }
        }
    }
}