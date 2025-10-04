using System;
using System.Diagnostics;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace DocControlService.Services
{
    public class NetworkShareService
    {
        /// <summary>
        /// Відкрити (створити) шар: створює папку (якщо потрібно), дає NTFS права Everyone:Modify і створює net share.
        /// Повертає true — якщо успішно створено або уже є.
        /// !!! Процес повинен мати права адміністратора.
        /// </summary>
        public bool OpenShare(string shareName, string folderPath)
        {
            try
            {
                Console.WriteLine($"[NetworkShareService] OpenShare: {shareName} -> {folderPath}");

                EnsureFolderExists(folderPath);

                // Надаємо NTFS права Everyone (Modify)
                try
                {
                    GrantModifyToEveryone(folderPath);
                    Console.WriteLine("[NetworkShareService] NTFS права для Everyone застосовано.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NetworkShareService] Помилка при встановленні NTFS прав: {ex.Message}");
                    // продовжуємо, бо net share може бути все одно створений
                }

                // Якщо шар вже існує — ок
                if (ShareExists(shareName))
                {
                    Console.WriteLine($"[NetworkShareService] Share '{shareName}' вже існує.");
                    // В якості захисту можна перевірити що шлях співпадає — пропускаємо зараз
                    return true;
                }

                // Створюємо шар
                var args = $"share {EscapeArg(shareName)}=\"{EscapeArg(folderPath)}\" /grant:Everyone,full";
                var (code, stdout, stderr) = RunProcess("net", args);
                if (code != 0)
                {
                    Console.WriteLine($"[NetworkShareService] Помилка net share: exit={code}, err={stderr}, out={stdout}");
                    return false;
                }

                Console.WriteLine($"[NetworkShareService] Share створено: \\\\{Environment.MachineName}\\{shareName}");

                // Включаємо файл/принтер правило брандмауера (спроба)
                try
                {
                    EnableFileAndPrinterFirewallRule();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[NetworkShareService] Помилка при увімкненні firewall rule: {ex.Message}");
                }

                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkShareService] OpenShare unexpected error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Видалити шар. Повертає true якщо видалено або не існував.
        /// </summary>
        public bool CloseShare(string shareName)
        {
            try
            {
                Console.WriteLine($"[NetworkShareService] CloseShare: {shareName}");
                if (!ShareExists(shareName))
                {
                    Console.WriteLine($"[NetworkShareService] Share '{shareName}' не існує — нічого робити.");
                    return true;
                }

                var (code, stdout, stderr) = RunProcess("net", $"share {EscapeArg(shareName)} /delete");
                if (code != 0)
                {
                    Console.WriteLine($"[NetworkShareService] Помилка видалення share: exit={code}, err={stderr}, out={stdout}");
                    return false;
                }

                Console.WriteLine($"[NetworkShareService] Share '{shareName}' видалено.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkShareService] CloseShare unexpected error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Повертає true якщо шар з іменем shareName є (за списком net share).
        /// </summary>
        public bool ShareExists(string shareName)
        {
            try
            {
                var (code, stdout, stderr) = RunProcess("net", "share");
                if (code != 0)
                {
                    Console.WriteLine($"[NetworkShareService] Не вдалось отримати список шарів: {stderr}");
                    return false;
                }

                using var sr = new StringReader(stdout);
                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    var trimmed = line.TrimStart();
                    if (trimmed.StartsWith(shareName + " ", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith(shareName + "\t", StringComparison.OrdinalIgnoreCase) ||
                        trimmed.StartsWith(shareName + "\r", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[NetworkShareService] ShareExists error: {ex.Message}");
                return false;
            }
        }

        // --- допоміжні методи ---

        private void EnsureFolderExists(string folderPath)
        {
            if (!Directory.Exists(folderPath))
            {
                Console.WriteLine($"[NetworkShareService] Папки немає — створюю: {folderPath}");
                Directory.CreateDirectory(folderPath);
            }
        }

        private void GrantModifyToEveryone(string folderPath)
        {
            var dirInfo = new DirectoryInfo(folderPath);
            var acl = dirInfo.GetAccessControl();

            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var rule = new FileSystemAccessRule(
                everyone,
                FileSystemRights.Modify | FileSystemRights.Synchronize,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow);

            bool modified = acl.ModifyAccessRule(AccessControlModification.Add, rule, out bool result);
            // У деяких системах ModifyAccessRule повертає false, але SetAccessControl працює — спробуємо все одно встановити
            dirInfo.SetAccessControl(acl);
        }

        private void EnableFileAndPrinterFirewallRule()
        {
            // Це включає групу правил для File and Printer Sharing (netsh advfirewall ...)
            var (code, stdout, stderr) = RunProcess("netsh", "advfirewall firewall set rule group=\"File and Printer Sharing\" new enable=Yes");
            if (code != 0)
            {
                Console.WriteLine($"[NetworkShareService] netsh returned non-zero: {stderr} {stdout}");
            }
            else
            {
                Console.WriteLine("[NetworkShareService] Firewall: File and Printer Sharing rules enabled.");
            }
        }

        /// <summary>
        /// Запуск процесу та збір виводу. Працює з UseShellExecute = false (не використовує Verb).
        /// Процес повинен запускатися з правами admin, якщо потрібні admin-команди.
        /// </summary>
        private (int exitCode, string stdout, string stderr) RunProcess(string fileName, string arguments)
        {
            using var p = new Process();
            p.StartInfo.FileName = fileName;
            p.StartInfo.Arguments = arguments;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.CreateNoWindow = true;

            var sbOut = new StringBuilder();
            var sbErr = new StringBuilder();

            p.OutputDataReceived += (s, e) => { if (e.Data != null) sbOut.AppendLine(e.Data); };
            p.ErrorDataReceived += (s, e) => { if (e.Data != null) sbErr.AppendLine(e.Data); };

            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            p.WaitForExit();

            return (p.ExitCode, sbOut.ToString(), sbErr.ToString());
        }

        private string EscapeArg(string s)
        {
            // Просте ескейпінг для лапок
            return s.Replace("\"", "\\\"");
        }
    }
}
