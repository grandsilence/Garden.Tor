using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Garden.Tor.IO;

namespace Garden.Tor
{
    // TODO: copy cache instead of new (faster) 
    public delegate void DTorLog(string text);

    internal abstract class BaseTor
    {
        #region Public
        /// <summary>
        /// Password used in config.cfg
        /// </summary>
        public string RemotePassword = "Garden.Tor";

        /// <summary>
        /// SOCKS 4a Tor proxy port.
        /// </summary>
        public ushort PortProxy {
            get => _portProxy == 0 ? _portProxyOverride : _portProxy;
            set => _portProxyOverride = value;
        }

        /// <summary>
        /// Remote Tor control port.
        /// </summary>
        public ushort PortControl {
            get => _portControl == 0 ? _portControlOverride : _portControl;
            set => _portControlOverride = value;
        }

        /// <summary>
        /// Binding interface address used for remote control and proxy address.
        /// </summary>
        public static readonly string InterfaceAddress = "127.0.0.1";

        /// <summary>
        /// Full Socks 4a Tor proxy address including port.
        /// </summary>
        public string ProxyAddress { get; private set; }

        /// <summary>
        /// Serial number of tor instance.
        /// </summary>
        public ushort Slot {
            get => _slot == 0 ? _slotOverride : _slot;
            set => _slotOverride = value;
        }

        /// <summary>
        /// Tor connection progress.
        /// </summary>
        public byte Progress { get; private set; }

        /// <summary>
        /// Tor Event on progress connection event.
        /// </summary>
        public event EventHandler<byte> OnProgress;
        #endregion

        #region Multithreaded Tor instances sync
        // Блокировщик потока когда надо получить слот или порты
        private static readonly object LockerSlot = new object();
        //private static readonly object LockerPort = new object();
        private static readonly object LockerKill = new object();
        #endregion

        // При первом запуске, первого экземпляра тора очищаем все старые tor.exe
        private static bool _killOldTors = true;
        private FileStream _lock;
    
        // Порты которые непосредственно используются тором
        private ushort _portProxy, _portControl,
            // которые заданны пользователем, через поля, чтобы переопеделять автовыбор порта т.е. порты указаны вручную
            _portProxyOverride, _portControlOverride;

        // Слот
        private ushort _slot, _slotOverride;

        private readonly DTorLog _log;
        
        // Переменные связанные с процессом Tor.exe 
        private Process _process;
        private bool _running = false;        
        private string _torExeLinkPath;

        // Обработчики при закрытии программы консоли, храним потому что сборщик очищает
        private ConsoleHandlerRoutine _consoleExitConsoleHandler;

        /// <summary>
        /// Подготовка экземпляра Tor для дальнейшего запуска через метод Start().
        /// </summary>
        /// <param name="log">Метод для вывода лога, null если отключить вывод.</param>
        protected BaseTor(DTorLog log = null) {
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
            lock (LockerSlot) {
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
            lock (LockerKill) {
                if (_killOldTors) {
                    _killOldTors = false;

                    KillAll();

                    if (ResourceHelper.IsConsoleApplication) {
                        // Используем переменные для Handler'ов потому что сборщик иначе удаляет
                        _consoleExitConsoleHandler = new ConsoleHelper.HandlerRoutine((ControlTypes ctrlType) => {
                            OnTerminate();
                            return true;
                        });
                        SetConsoleCtrlHandler(_consoleExitConsoleHandler, true);
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

            _proxyAddress = InterfaceAddress + ":" + _portProxy;

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
                _tcpSocket.Connect(InterfaceAddress, _portControl);
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

       
        #region IDisposable Support Pattern
        private bool _disposed;

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) 
                return;

            // Release managed objects
            if (disposing)
            {
                // disp remote client
            }

            // Set big fields to NULL here:
            _disposed = true;
        }
        
        public void Dispose()
        {
            Dispose(true);
        }
        #endregion
    }
}
