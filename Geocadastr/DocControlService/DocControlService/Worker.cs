using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Services;

namespace DocControlService
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly DatabaseManager _db;
        private readonly DirectoryRepository _dirRepo;
        private readonly AccessService _accessService;
        private readonly NetworkShareService _netShare;
        private readonly VersionControlFactory _vcsFactory;

        public Worker(
            ILogger<Worker> logger,
            DatabaseManager db,
            DirectoryRepository dirRepo,
            AccessService accessService,
            NetworkShareService netShare,
            VersionControlFactory vcsFactory)
        {
            _logger = logger;
            _db = db;
            _dirRepo = dirRepo;
            _accessService = accessService;
            _netShare = netShare;
            _vcsFactory = vcsFactory;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Console.WriteLine("▶ Worker starting...");

            // 1) Сканування всіх директорій
            var scanner = new DirectoryScanner(_db);
            var directories = _dirRepo.GetAllDirectories();

            foreach (var dir in directories)
            {
                Console.WriteLine($"➡ Скануємо {dir.Name} ({dir.Browse})...");
                scanner.ScanDirectoryById(dir.Id);
            }

            // 2) Синхронізація доступів
            _accessService.SyncAccessTable();

            // 3) Відкрити всі директорії в мережі
            foreach (var dir in _accessService.GetSharedDirectories())
            {
                string shareName = $"Dir_{dir.Id}";
                _netShare.OpenShare(shareName, dir.Path);
            }
            Console.WriteLine("✅ Всі директорії відкрито в мережі.");

            // 4) Перевірка git-сервісів (щоб не було "Repo not ready")
            foreach (var vcs in _vcsFactory.GetAllServices())
            {
                vcs.CommitAll("Initial auto-check commit");
            }

            Console.WriteLine("\n=== Worker loop started ===");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    var shared = _accessService.GetSharedDirectories();
                    Console.WriteLine($"[{DateTime.Now}] Відкрито {shared.Count} директорій.");

                    // Автокоміти кожні 60 хв
                    foreach (var vcs in _vcsFactory.GetAllServices())
                    {
                        vcs.CommitAll("Auto commit by service");
                    }

                    await Task.Delay(TimeSpan.FromMinutes(60), stoppingToken);
                }
            }
            finally
            {
                // Закрити всі шари перед зупинкою
                foreach (var dir in _accessService.GetSharedDirectories())
                {
                    string shareName = $"Dir_{dir.Id}";
                    _netShare.CloseShare(shareName);
                }
                Console.WriteLine("🛑 Всі директорії закриті (сервіс зупинено).");
            }
        }
    }
}
