using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Garden.Tor
{
    internal abstract class BaseTor
    {
         //TODO: copy cache instead of new (faster)  

        //
        // Public
        public string RemotePassword = "howwedo5";

        public ushort PortProxy
        {
            get => _portProxy == 0 ? _portProxyOverride : _portProxy;
            set => _portProxyOverride = value;
        }

        public ushort PortControl
        {
            get => _portControl == 0 ? _portControlOverride : _portControl;
            set => _portControlOverride = value;
        }

        public string ProxyAddress => _proxyAddress;

        public ushort Slot {
            get { return _slot == 0 ? _slotOverride : _slot; }
            set { _slotOverride = value; }
        }
        
        public byte Progress {
            get { return _progress; }
            private set { _progress = value; }
        }

        public event EventHandler<byte> OnProgress;

        //
        // Protected
        protected const string _interfaceAddress = "127.0.0.1";

        //
        // Private

        // Блокировщик потока когда надо получить слот или порты
        static object _lockerSlot = new object();
        static object _lockerPort = new object();
        static object _killLocker = new object();
        //static object _cacheLocker = new object();

        // При первом запуске, первого экземпляра тора очищаем все старые tor.exe
        static bool _killOldTors = true;

        FileStream _lock = null;
    
        // Порты
        // которые непосредственно используются тором
        ushort _portProxy = 0, _portControl = 0;
        // которые заданны пользователем, через поля, чтобы переопеделять автовыбор порта т.е. порты указаны вручную
        ushort _portProxyOverride = 0, _portControlOverride = 0;
        // Полный адрес прокси, "адрес_интерфейса:порт"
        string _proxyAddress = null;

        // Слот
        ushort _slot = 0;
        ushort _slotOverride = 0;
        static ushort _slotCached = 0;

        // Прогресс 
        byte _progress = 0;

        DLog _log;
        
        // Переменные связанные с процессом Tor.exe 
        Process _process;
        bool _running = false;        
        string _torExeLinkPath;

        // Обработчики при закрытии программы консоли, храним потому что сборщик очищает
        HandlerRoutine _consoleExitHandler;

        /// <summary>
        /// Подготовка экземпляра Tor для дальнейшего запуска через метод Start().
        /// </summary>
        /// <param name="log">Метод для вывода лога, null если отключить вывод.</param>
        public TorBase(DLog log = null) {
            _log = log;
        }

        bool CreateSlotLocker(ushort slot) {
            string path = "tor_files\\cache\\lock" + slot;

            if (!File.Exists(path)) {
                try {
                    _lock = File.Create(path, 1, FileOptions.DeleteOnClose);

                    return true;
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
                catch (Exception) {
                    throw;
                }
            }
                           
            return false;
        }

        ushort GetAvailableSlot() {
            ushort slot;
            lock (_lockerSlot) {
                for (slot = 1; !CreateSlotLocker(slot) || !ResourceHelper.TryDelete($"tor_files\\cache\\{slot}\\lock"); slot++);
            }

            return slot;
        }

        /// <summary>
        /// Убивает все запущенные процессы тора. Используется после выхода из программы и перед стартом, чтобы всё чисто было.
        /// </summary>
        /// <returns>Имя процесса который был убит</returns>
        static string KillAll() {
            string currentProcess = Process.GetCurrentProcess().ProcessName.Replace(".vshost", "");
            string torExeLink = "tor4_" + currentProcess;

            Parallel.ForEach(Process.GetProcessesByName(torExeLink), p => {
                p.Kill();
                p.WaitForExit();
            });

            return torExeLink;
        }


        /// <summary>
        /// Вызывается когда программа была неожиданно завершена или консольное приложение было закрыто.
        /// </summary>
        protected static void OnTerminate() {
            string path = KillAll() + ".exe";

            ResourceHelper.TryDelete("tor_files\\" + path);
        }

        private void ReadLineTorConsole(ref string line, DBool action) {
            var sw = Stopwatch.StartNew();

            do {
                line = _process.StandardOutput.ReadLine();

                if (line.Contains("[error] "))
                    throw new TorException(line.Substring("[error] ", "."));

                if (action())
                    return;

            } while (sw.ElapsedMilliseconds < 120000);

            throw new TorException("Код на чтение консоли тора выполнялся более 120 сек.");
        }

        /// <summary>
        /// Инициализировать Tor, создав процесс и подключившись к нему для дальнейшего управления через Telnet.
        /// </summary>
        /// <param name="showWindow">Отображать окно консоли Tor.</param>
        /// <param name="shellExecute">Запуск при помощи оболочки Windows. Используйте true для Console Application.</param>
        public virtual void Start(bool showWindow = false, bool shellExecute = false) {
            // Провека на запущенность Tor
            if (_running && !_process.HasExited)
                throw new OperationCanceledException("Tor уже запущен, для создания нового Tor'a создайте новый экземпляр класса");

            // Проверка существования папки Tor
            string torFolderPath = Directory.GetCurrentDirectory() + @"\tor_files\";
            if (!Directory.Exists(torFolderPath))
                throw new DirectoryNotFoundException("Не найдена папка \"tor_files\". Создайте её и поместите внутрь \"tor.exe\" (желательно взломанный) и \"config.cfg\".");

            // Проверка существования tor.exe и config.cfg
            string[] ways = new string[] { torFolderPath + "tor.exe", torFolderPath + "config.cfg" };
            foreach (string path in ways) {
                if (!File.Exists(path))
                    throw new FileNotFoundException($"Не найден файл \"{path}\".", path);
            }

            // Наименование процессов Tor должны отличаться, чтобы не было конфликтов            
            // Новое ммя будем брать на основе запущенного проекта exe, убираем vshost чтобы не было различий Debug и Release
            string currentProcess = Process.GetCurrentProcess().ProcessName.Replace(".vshost", "");
            string torExeLink = "tor4_" + currentProcess;
            _torExeLinkPath = torFolderPath + torExeLink  + ".exe";

            // Убиваем все старые торы перед запуском и после завершения работы проги
            lock (_killLocker) {
                if (_killOldTors) {
                    _killOldTors = false;

                    KillAll();

                    if (ResourceHelper.IsConsoleApplication) {
                        // Используем переменные для Handler'ов потому что сборщик иначе удаляет
                        _consoleExitHandler = new HandlerRoutine((ControlTypes ctrlType) => {
                            OnTerminate();
                            return true;
                        });
                        SetConsoleCtrlHandler(_consoleExitHandler, true);
                    }

                    // Forms Exit
                    AppDomain.CurrentDomain.ProcessExit += new EventHandler((object o, EventArgs a) => {
                        OnTerminate();
                    });

                    // Exception Exit (All apps)
                    AppDomain.CurrentDomain.UnhandledException += new UnhandledExceptionEventHandler((object o, UnhandledExceptionEventArgs args) => {
                        OnTerminate();
                    });                    
                }

                // Создаем папку с кешем если её нет, в случае автоматических слотов - иначе будет Exception
                const string cacheDirectory = "tor_files\\cache";
                if (!Directory.Exists(cacheDirectory))
                    Directory.CreateDirectory(cacheDirectory);
            }

            // Создаем жесткую ссылку для tor.exe, чтобы имена процессов отличались
            ResourceHelper.MakeHardLink(ways[0], _torExeLinkPath);

            // Получаем доступный слот для папки кеша
            _slot = _slotOverride == 0 ? GetAvailableSlot() : _slotOverride;
            
            // Инициализация процесса tor.exe с параметрами: управляющий порт Telnet, порт прокси Tor и путь к мусору и конфигу            
            ProcessStartInfo info = new ProcessStartInfo(_torExeLinkPath);
            //string quiet = !showWindow ? " --quiet" : "";            
            info.Arguments = $"--DataDirectory \"tor_files\\cache\\{_slot}\""
                             + " -f \"tor_files\\config.cfg\"";

            // Переопределяем порты, если они заданы пользователем
            if (_portControlOverride != 0)
                info.Arguments += $" --ControlPort \"{_portControl}\"";
            if (_portProxyOverride != 0)
                info.Arguments += $" --SocksPort \"{_portProxy}";
            
            /*
            info.Arguments = $"--DataDirectory \"tor_files\\cache\\{_slot}\" --ControlPort \"{_portControl}\" --SocksPort \"{_portProxy}"
                 + "\" -f \"tor_files\\config.cfg\"" + quiet;
            */

            info.UseShellExecute = shellExecute;
            info.CreateNoWindow = !showWindow;
            info.RedirectStandardOutput = true;

            // Запуск tor.exe
            Log("Запуск процесса Tor...");
            _process = Process.Start(info);
                             
            _running = true;

            // Получаем порты на которых запущен тор       
            string line = null;

            // Читаем порт прокси
            if (_portProxyOverride == 0) {
                ReadLineTorConsole(ref line, () => {
                    bool result = line.Contains("Socks listener listening on port");
                    if (result)
                        _portProxy = ushort.Parse(line.Substring("port ", "."));

                    return result;
                });
            } else {
                _portProxy = _portProxyOverride;
            }

            // Читаем порт Telnet
            if (_portControlOverride == 0) {
                ReadLineTorConsole(ref line, () => {
                    bool result = line.Contains("Control listener listening on port");
                    if (result)
                        _portControl = ushort.Parse(line.Substring("port ", "."));

                    return result;
                });
            } else {
                _portControl = _portControlOverride;
            }

            // Читаем прогресс подключения к сети Tor
            ReadLineTorConsole(ref line, () => {
                if (line.Contains("Bootstrapped ")) {
                    Progress = byte.Parse(line.Substring("Bootstrapped ", "%"));
                    Log($"Подключение к Tor в процессе: {Progress}%");

                    OnProgress?.Invoke(this, Progress);
                }
                                             
                return (Progress == 100);
            });

            _proxyAddress = _interfaceAddress + ":" + _portProxy;

            // Подключаемся и настраиваемся на сервер Telnet
            RemoteInit();
        }

        /// <summary>
        /// Выполнение кода с ожиданием, если результат был не успешен. Ожидание ограничено попытками и интервалом исполнения кода.
        /// </summary>
        /// <param name="action">Код который должен успешно выполнится в течение определенного времени</param>
        /// <param name="exceptionMessage">Сообщение исключения, когда так и не выполнился успешно</param>
        /// <param name="intervalMs">Период исполнения кода</param>
        /// <param name="retries">Число попыток</param>
        void WaitFor(DBool action, string exceptionMessage, int intervalMs = 1000, int retries = 10) {
            do {
                if (action())
                    return;

                Thread.Sleep(intervalMs);
            } while (retries-- > 0);

            throw new TorException(exceptionMessage);
        }

        void RemoteInit() {
            // Ждем подключения по Telnet
            Log("Подключаемся по Telnet к управляющему серверу Tor...");
            WaitFor(() => {
                _tcpSocket.Connect(_interfaceAddress, _portControl);
                return _tcpSocket.Connected;
            }, "Невозможно подключиться по Telnet к Tor в течение 10 секунд.", 500, 20);

            // Авторизация
            Log("Авторизация с управляющим сервером Tor...");
            WaitFor(() => {
                RemoteWriteLine($"AUTHENTICATE \"{RemotePassword}\"");
                return IsRemoteAnswer("250");
            }, "Ошибка авторизации Telnet при подключении к Tor. Истекло 10 секунд.");

            // TODO?: if non auth -> Disconnect()

            // Включаем уведомления о событиях
            RemoteWriteLine("SETEVENTS SIGNAL");

            // Закрываем Tor.exe, как только отключаемся от Telnet сервера
            RemoteWriteLine("TAKEOWNERSHIP");

            // Ждем соединение с сетью Tor
            // TODO?: ВОТ ЭТО МОЖНО ОБЕРНУТЬ В ЦИКЛ, do while + try,catch. И делать refreshIp в catch. Так было в старой версии
            Log("Поключение к внутренней сети Tor...");
            WaitFor(() => {
                RemoteWriteLine("getinfo status/circuit-established");
                return IsRemoteAnswer("established=1");
            }, "Невозможно установить соединение с сетью Tor. Истекло 100 секунд", 1000, 100);

            Log("Успешно подключился к внутренней сети Tor");
        }


        /// <summary>
        /// Завершить процесс Tor
        /// </summary>
        /// <param name="force">False закрывает окно Tor (безопаснее), True принудительно убивает процесс/param>
        /// <param name="waitForExit">Ждать завершения процесса Tor</param>
        public void Stop(bool force = false, bool waitForExit = true) {
            Log($"В процессе {(force ? "принудильная" : "мягкая")} остановка процесса Tor...");
            _running = false;
            Progress = 0;
            _lock?.Dispose();

            if (!_process.HasExited) {
                if (force)
                    _process.Kill();
                else
                    _process.CloseMainWindow();

                if (waitForExit)
                    _process.WaitForExit();                    

                Log("Процесс Tor был завершен");
            } else {
                Log("Процесс Tor не запущен, нечего останавливать!");
            }                
        }        

        /// <summary>
        /// Вывод в лог произвольного сообщения, если задан способ вывода через делегат.
        /// </summary>
        /// <param name="message">Сообщение</param>
        protected void Log(string message) {
            _log?.Invoke(message);
        }

       

        #region Disposing
        bool _disposed = false;

        // Public implementation of Dispose pattern callable by consumers.
        public void Dispose() {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        // Protected implementation of Dispose pattern.
        protected virtual void Dispose(bool disposing) {
            if (_disposed)
                return;

            if (disposing) {
                // Disconnect from remote Telnet Tor Control
                Disconnect();
                // Stop tor process and wait for exit
                Stop(force: false, waitForExit: true);
                // Delete hard link
                ResourceHelper.TryDelete(_torExeLinkPath);
            }

            // Free any unmanaged objects here.
            //
            _disposed = true;
        }

        void Disconnect() {
            if (_tcpSocket.Connected) {
                RemoteWriteLine("QUIT");

                // Dispose Tcp Socket
                _tcpSocket.Close();               
            }
        }
        #endregion
    }
    }
}
