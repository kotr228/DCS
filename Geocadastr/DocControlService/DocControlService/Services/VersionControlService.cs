// File: Services/VersionControlService.cs
using LibGit2Sharp;
using System;
using System.IO;

namespace DocControlService.Services
{
    public class VersionControlService
    {
        private readonly string _repoPath;
        private Repository? _repo;

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
                    return;
                }

                string gitPath = Path.Combine(_repoPath, ".git");

                if (Repository.IsValid(gitPath))
                {
                    _repo = new Repository(_repoPath);
                    Console.WriteLine($"✅ Репозиторій знайдено у {_repoPath}");
                }
                else
                {
                    Console.WriteLine($"📦 Створюємо новий git-репозиторій у {_repoPath}...");
                    Repository.Init(_repoPath);
                    _repo = new Repository(_repoPath);

                    CommitAll("Initial commit");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка ініціалізації git у {_repoPath}: {ex.Message}");
                _repo = null;
            }
        }

        public void CommitAll(string message = "Автоматичний коміт")
        {
            if (_repo == null)
            {
                Console.WriteLine("❌ Репозиторій не готовий, комміт неможливий.");
                return;
            }

            try
            {
                // Стадім усі файли, але ігноруємо помилки доступу
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
                }
                catch (EmptyCommitException)
                {
                    Console.WriteLine("ℹ Немає змін для коміту.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка при коміті: {ex.Message}");
            }
        }

        public void ShowLog()
        {
            if (_repo == null)
            {
                Console.WriteLine("❌ Репозиторій не готовий.");
                return;
            }

            foreach (var commit in _repo.Commits)
            {
                Console.WriteLine($"{commit.Author.When}: {commit.MessageShort}");
            }
        }
    }
}
