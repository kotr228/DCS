using LibGit2Sharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DocControlService.Services
{
    public class VersionControlService
    {
        private readonly string _repoPath;
        private Repository? _repo;
        public event Action<string, string, string> OnCommitStatusChanged; // directoryPath, status, message

        public VersionControlService(string repoPath)
        {
            _repoPath = repoPath;
            InitializeRepository();
        }

        private void InitializeRepository()
        {
            try
            {
                if (!Directory.Exists(_repoPath))
                {
                    Console.WriteLine($"❌ Директорія {_repoPath} не існує.");
                    OnCommitStatusChanged?.Invoke(_repoPath, "error", "Директорія не існує");
                    return;
                }

                string gitPath = Path.Combine(_repoPath, ".git");

                if (Repository.IsValid(gitPath))
                {
                    _repo = new Repository(_repoPath);
                    Console.WriteLine($"✅ Репозиторій знайдено у {_repoPath}");
                    OnCommitStatusChanged?.Invoke(_repoPath, "success", "Репозиторій ініціалізовано");
                }
                else
                {
                    Console.WriteLine($"📦 Створюємо новий git-репозиторій у {_repoPath}...");
                    Repository.Init(_repoPath);
                    _repo = new Repository(_repoPath);

                    CommitAll("Initial commit");
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"❌ Немає доступу до {_repoPath}");
                OnCommitStatusChanged?.Invoke(_repoPath, "denied", "Немає прав доступу до директорії");
                _repo = null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка ініціалізації git у {_repoPath}: {ex.Message}");
                OnCommitStatusChanged?.Invoke(_repoPath, "error", $"Помилка: {ex.Message}");
                _repo = null;
            }
        }

        public void CommitAll(string message = "Автоматичний коміт")
        {
            if (_repo == null)
            {
                Console.WriteLine("❌ Репозиторій не готовий, комміт неможливий.");
                OnCommitStatusChanged?.Invoke(_repoPath, "error", "Репозиторій не ініціалізовано");
                return;
            }

            try
            {
                try
                {
                    Commands.Stage(_repo, "*");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠ Попередження при stage: {ex.Message}");
                }

                var author = new Signature("DocService", "service@local", DateTime.Now);

                try
                {
                    _repo.Commit(message, author, author);
                    Console.WriteLine($"✅ Зроблено коміт: {message}");
                    OnCommitStatusChanged?.Invoke(_repoPath, "success", message);
                }
                catch (EmptyCommitException)
                {
                    Console.WriteLine("ℹ Немає змін для коміту.");
                    OnCommitStatusChanged?.Invoke(_repoPath, "success", "Немає змін");
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"❌ Немає доступу для коміту у {_repoPath}");
                OnCommitStatusChanged?.Invoke(_repoPath, "denied", "Немає прав доступу");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка при коміті: {ex.Message}");
                OnCommitStatusChanged?.Invoke(_repoPath, "error", ex.Message);
            }
        }

        public List<(string Hash, string Message, string Author, DateTime Date)> GetCommitHistory(int limit = 50)
        {
            var result = new List<(string, string, string, DateTime)>();

            if (_repo == null)
            {
                Console.WriteLine("❌ Репозиторій не готовий.");
                return result;
            }

            try
            {
                foreach (var commit in _repo.Commits.Take(limit))
                {
                    result.Add((
                        commit.Sha,
                        commit.MessageShort,
                        commit.Author.Name,
                        commit.Author.When.DateTime
                    ));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка отримання історії: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Повертає детальну історію комітів разом зі списком змінених файлів у кожному коміті.
        /// Для кожного коміту виконується diff з батьківським, щоб визначити Added/Modified/Deleted/Renamed.
        /// </summary>
        /// <param name="limit">Максимальна кількість комітів</param>
        public List<(string Hash, string Message, string Author, DateTime Date, List<(string Path, string ChangeType)> Files)> GetDetailedCommitHistory(int limit = 100)
        {
            var result = new List<(string, string, string, DateTime, List<(string, string)>)>();

            if (_repo == null)
            {
                Console.WriteLine("❌ Репозиторій не готовий.");
                return result;
            }

            try
            {
                foreach (var commit in _repo.Commits.Take(limit))
                {
                    var files = new List<(string Path, string ChangeType)>();

                    try
                    {
                        // Порівнюємо дерево коміту з батьківським (або з порожнім для початкового коміту)
                        var parentTree = commit.Parents.Any() ? commit.Parents.First().Tree : null;
                        var diff = _repo.Diff.Compare<TreeChanges>(parentTree, commit.Tree);

                        foreach (var change in diff.Added)
                            files.Add((change.Path, "Додано"));

                        foreach (var change in diff.Modified)
                            files.Add((change.Path, "Змінено"));

                        foreach (var change in diff.Deleted)
                            files.Add((change.Path, "Видалено"));

                        foreach (var change in diff.Renamed)
                            files.Add((change.Path, "Перейменовано"));
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"⚠ Не вдалося отримати diff для коміту {commit.Sha.Substring(0, 7)}: {ex.Message}");
                    }

                    result.Add((
                        commit.Sha,
                        commit.MessageShort,
                        commit.Author.Name,
                        commit.Author.When.DateTime,
                        files
                    ));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка отримання детальної історії: {ex.Message}");
            }

            return result;
        }

        public bool RevertToCommit(string commitHash)
        {
            if (_repo == null)
            {
                Console.WriteLine("❌ Репозиторій не готовий.");
                return false;
            }

            try
            {
                var commit = _repo.Lookup<Commit>(commitHash);
                if (commit == null)
                {
                    Console.WriteLine($"❌ Коміт {commitHash} не знайдено");
                    return false;
                }

                Commands.Checkout(_repo, commit);
                Console.WriteLine($"✅ Відкат до коміту {commitHash.Substring(0, 7)} виконано");
                OnCommitStatusChanged?.Invoke(_repoPath, "success", $"Відкат до {commitHash.Substring(0, 7)}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка відкату: {ex.Message}");
                OnCommitStatusChanged?.Invoke(_repoPath, "error", $"Помилка відкату: {ex.Message}");
                return false;
            }
        }

        public string GetStatus()
        {
            if (_repo == null)
                return "Не ініціалізовано";

            try
            {
                var status = _repo.RetrieveStatus();
                if (!status.IsDirty)
                    return "Чисто";

                return $"Змін: {status.Count()}";
            }
            catch
            {
                return "Помилка";
            }
        }

        public void ShowLog()
        {
            if (_repo == null)
            {
                Console.WriteLine("❌ Репозиторій не готовий.");
                return;
            }

            foreach (var commit in _repo.Commits.Take(10))
            {
                Console.WriteLine($"{commit.Sha.Substring(0, 7)} {commit.Author.When:yyyy-MM-dd HH:mm}: {commit.MessageShort}");
            }
        }
    }
}