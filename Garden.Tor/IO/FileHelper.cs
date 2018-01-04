using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Garden.Tor.IO
{
    internal static class FileHelper
    {
        /// <summary>
        /// Функция нужна для удаления жесткой ссылки на tor.exe и проверки занятости каталога Data.
        /// </summary>
        /// <param name="filePath">Относительный путь к файлу</param>
        /// <returns>Вернет True если было удаление или файл не найден.</returns>
        public static bool TryDelete(string filePath) {
            try {
                File.Delete(filePath);
                return true;
            }
            catch (DirectoryNotFoundException) {
                return true;
            }            
            catch (Exception ex) {
                // Если файл используется или запущен
                if (ex is IOException || ex is UnauthorizedAccessException)
                    return false;

                throw;
            }
        }

        /// <summary>
        /// Создаёт жесткую ссылку на файл не копируя его относительно текущей директории. Нужно для уникального имени Tor.exe во всех проектах.
        /// </summary>
        /// <param name="source">Исходный файл на который будет ссылка</param>
        /// <param name="destonation">Путь где будет создана ссылка на исходный файла</param>
        /// <returns>Вернёт true если ссылка существует или была создана.</returns>
        public static bool MakeHardLink(string source, string destonation) {        
            if (!File.Exists(source))
                throw new FileNotFoundException($"Не найден файл {source} для создания жесткой ссылки", source);

            return (File.Exists(destonation)) || CreateHardLink(destonation, source, IntPtr.Zero);
        }

        #region WinAPI
        // Create Hard Link for Tor.exe
        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool CreateHardLink(string lpFileName, string lpExistingFileName, IntPtr lpSecurityAttributes);
        #endregion
    }
}