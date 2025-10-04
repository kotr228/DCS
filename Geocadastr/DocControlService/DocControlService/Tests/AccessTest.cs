using DocControlService.Data;
using DocControlService.Models;
using DocControlService.Services;

namespace DocControlService.Tests
{
    public class AccessTest
    {
        private readonly AccessService _accessService;

        public AccessTest(DatabaseManager db)
        {
            _accessService = new AccessService(db);
        }

        public void Run()
        {
            Console.WriteLine("=== ТЕСТ ДОСТУПУ ДО ДИРЕКТОРІЙ ===");

            Console.WriteLine("➡ Відкриваємо всі директорії...");
            _accessService.OpenAll();

            var shared = _accessService.GetSharedDirectories();
            Console.WriteLine($"✅ Відкрито {shared.Count} директорій:");

            foreach (var d in shared)
            {
                Console.WriteLine($"   - {d.Id}: {d.Name} ({d.Path})");
            }

            Console.WriteLine("➡ Закриваємо всі директорії...");
            _accessService.CloseAll();

            shared = _accessService.GetSharedDirectories();
            Console.WriteLine($"❌ Тепер відкрито {shared.Count} директорій.");
        }
    }
}
