using DocControlService.Shared;
using System;
using System.IO;
using System.Text.Json;

namespace DocControlAI.Services
{
    /// <summary>
    /// Сервіс для експорту AI даних в JSON
    /// </summary>
    public class DataExportService
    {
        public string ExportChronologicalRoadmap(AIChronologicalRoadmap roadmap)
        {
            try
            {
                var options = new JsonSerializerOptions
                {
                    WriteIndented = true,
                    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };

                return JsonSerializer.Serialize(roadmap, options);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Помилка експорту: {ex.Message}");
                return null;
            }
        }

        public bool SaveToFile(string json, string filePath)
        {
            try
            {
                File.WriteAllText(filePath, json);
                Console.WriteLine($"✅ Збережено: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Помилка збереження: {ex.Message}");
                return false;
            }
        }
    }
}