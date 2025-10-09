using DocControlService.Models;
using DocControlService.Shared;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace DocControlService.Data
{
    /// <summary>
    /// Репозиторій для AI аналізів
    /// </summary>
    public class AIAnalysisRepository
    {
        private readonly DatabaseManager _db;

        public AIAnalysisRepository(DatabaseManager db)
        {
            _db = db ?? throw new ArgumentNullException(nameof(db));
        }

        #region AI Analysis Results

        public int SaveAnalysisResult(AIAnalysisResult result)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txn;
                cmd.CommandText = @"
                    INSERT INTO AIAnalysisResults 
                    (directoryId, directoryPath, analysisDate, analysisType, summary, rawAIResponse, isProcessed)
                    VALUES (@dirId, @path, @date, @type, @summary, @raw, @processed);";

                cmd.Parameters.AddWithValue("@dirId", result.DirectoryId);
                cmd.Parameters.AddWithValue("@path", result.DirectoryPath);
                cmd.Parameters.AddWithValue("@date", result.AnalysisDate.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@type", result.Type.ToString());
                cmd.Parameters.AddWithValue("@summary", result.Summary ?? "");
                cmd.Parameters.AddWithValue("@raw", result.RawAIResponse ?? "");
                cmd.Parameters.AddWithValue("@processed", result.IsProcessed ? 1 : 0);
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT last_insert_rowid();";
                int analysisId = Convert.ToInt32(cmd.ExecuteScalar());

                // Зберігаємо рекомендації
                foreach (var rec in result.Recommendations)
                {
                    rec.AnalysisResultId = analysisId;
                    SaveRecommendation(rec, conn, txn);
                }

                // Зберігаємо порушення
                foreach (var violation in result.Violations)
                {
                    violation.AnalysisResultId = analysisId;
                    SaveViolation(violation, conn, txn);
                }

                txn.Commit();
                return analysisId;
            }
            catch
            {
                txn.Rollback();
                throw;
            }
        }

        private void SaveRecommendation(AIRecommendation rec, SqliteConnection conn, SqliteTransaction txn)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO AIRecommendations 
                (analysisResultId, title, description, type, actionJson, priority, isApplied, appliedAt)
                VALUES (@analysisId, @title, @desc, @type, @action, @priority, @applied, @appliedAt);";

            cmd.Parameters.AddWithValue("@analysisId", rec.AnalysisResultId);
            cmd.Parameters.AddWithValue("@title", rec.Title);
            cmd.Parameters.AddWithValue("@desc", rec.Description ?? "");
            cmd.Parameters.AddWithValue("@type", rec.Type.ToString());
            cmd.Parameters.AddWithValue("@action", rec.ActionJson ?? "");
            cmd.Parameters.AddWithValue("@priority", rec.Priority.ToString());
            cmd.Parameters.AddWithValue("@applied", rec.IsApplied ? 1 : 0);
            cmd.Parameters.AddWithValue("@appliedAt", rec.AppliedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? (object)DBNull.Value);
            cmd.ExecuteNonQuery();
        }

        private void SaveViolation(StructureViolation violation, SqliteConnection conn, SqliteTransaction txn)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO StructureViolations 
                (analysisResultId, filePath, violationType, description, suggestedPath, isResolved)
                VALUES (@analysisId, @path, @type, @desc, @suggested, @resolved);";

            cmd.Parameters.AddWithValue("@analysisId", violation.AnalysisResultId);
            cmd.Parameters.AddWithValue("@path", violation.FilePath);
            cmd.Parameters.AddWithValue("@type", violation.Type.ToString());
            cmd.Parameters.AddWithValue("@desc", violation.Description ?? "");
            cmd.Parameters.AddWithValue("@suggested", violation.SuggestedPath ?? "");
            cmd.Parameters.AddWithValue("@resolved", violation.IsResolved ? 1 : 0);
            cmd.ExecuteNonQuery();
        }

        public List<AIAnalysisResult> GetAnalysesByDirectory(int directoryId)
        {
            var results = new List<AIAnalysisResult>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, directoryId, directoryPath, analysisDate, analysisType, summary, rawAIResponse, isProcessed
                FROM AIAnalysisResults
                WHERE directoryId = @dirId
                ORDER BY analysisDate DESC;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var result = new AIAnalysisResult
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    DirectoryPath = reader.GetString(2),
                    AnalysisDate = DateTime.Parse(reader.GetString(3)),
                    Type = Enum.Parse<AIAnalysisType>(reader.GetString(4)),
                    Summary = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    RawAIResponse = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    IsProcessed = reader.GetInt32(7) == 1
                };
                results.Add(result);
            }

            return results;
        }

        #endregion

        #region Chronological Roadmaps

        public int SaveChronologicalRoadmap(AIChronologicalRoadmap roadmap)
        {
            using var conn = _db.GetConnection();
            conn.Open();
            using var txn = conn.BeginTransaction();

            try
            {
                using var cmd = conn.CreateCommand();
                cmd.Transaction = txn;
                cmd.CommandText = @"
                    INSERT INTO AIChronologicalRoadmaps 
                    (directoryId, name, description, generatedAt, aiInsights)
                    VALUES (@dirId, @name, @desc, @date, @insights);";

                cmd.Parameters.AddWithValue("@dirId", roadmap.DirectoryId);
                cmd.Parameters.AddWithValue("@name", roadmap.Name);
                cmd.Parameters.AddWithValue("@desc", roadmap.Description ?? "");
                cmd.Parameters.AddWithValue("@date", roadmap.GeneratedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("@insights", roadmap.AIInsights ?? "");
                cmd.ExecuteNonQuery();

                cmd.CommandText = "SELECT last_insert_rowid();";
                int roadmapId = Convert.ToInt32(cmd.ExecuteScalar());

                // Зберігаємо події
                foreach (var evt in roadmap.Events)
                {
                    evt.RoadmapId = roadmapId;
                    SaveChronologicalEvent(evt, conn, txn);
                }

                txn.Commit();
                return roadmapId;
            }
            catch
            {
                txn.Rollback();
                throw;
            }
        }

        private void SaveChronologicalEvent(ChronologicalEvent evt, SqliteConnection conn, SqliteTransaction txn)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = txn;
            cmd.CommandText = @"
                INSERT INTO ChronologicalEvents 
                (roadmapId, eventDate, title, description, category, relatedFiles, aiGeneratedContext)
                VALUES (@roadmapId, @date, @title, @desc, @category, @files, @context);";

            cmd.Parameters.AddWithValue("@roadmapId", evt.RoadmapId);
            cmd.Parameters.AddWithValue("@date", evt.EventDate.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@title", evt.Title);
            cmd.Parameters.AddWithValue("@desc", evt.Description ?? "");
            cmd.Parameters.AddWithValue("@category", evt.Category ?? "");
            cmd.Parameters.AddWithValue("@files", JsonSerializer.Serialize(evt.RelatedFiles));
            cmd.Parameters.AddWithValue("@context", evt.AIGeneratedContext ?? "");
            cmd.ExecuteNonQuery();
        }

        public List<AIChronologicalRoadmap> GetChronologicalRoadmapsByDirectory(int directoryId)
        {
            var roadmaps = new List<AIChronologicalRoadmap>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, directoryId, name, description, generatedAt, aiInsights
                FROM AIChronologicalRoadmaps
                WHERE directoryId = @dirId
                ORDER BY generatedAt DESC;";
            cmd.Parameters.AddWithValue("@dirId", directoryId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var roadmap = new AIChronologicalRoadmap
                {
                    Id = reader.GetInt32(0),
                    DirectoryId = reader.GetInt32(1),
                    Name = reader.GetString(2),
                    Description = reader.IsDBNull(3) ? "" : reader.GetString(3),
                    GeneratedAt = DateTime.Parse(reader.GetString(4)),
                    AIInsights = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Events = GetChronologicalEvents(reader.GetInt32(0))
                };
                roadmaps.Add(roadmap);
            }

            return roadmaps;
        }

        private List<ChronologicalEvent> GetChronologicalEvents(int roadmapId)
        {
            var events = new List<ChronologicalEvent>();
            using var conn = _db.GetConnection();
            conn.Open();

            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT id, roadmapId, eventDate, title, description, category, relatedFiles, aiGeneratedContext
                FROM ChronologicalEvents
                WHERE roadmapId = @id
                ORDER BY eventDate;";
            cmd.Parameters.AddWithValue("@id", roadmapId);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                events.Add(new ChronologicalEvent
                {
                    Id = reader.GetInt32(0),
                    RoadmapId = reader.GetInt32(1),
                    EventDate = DateTime.Parse(reader.GetString(2)),
                    Title = reader.GetString(3),
                    Description = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Category = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    RelatedFiles = JsonSerializer.Deserialize<List<string>>(reader.GetString(6)) ?? new List<string>(),
                    AIGeneratedContext = reader.IsDBNull(7) ? "" : reader.GetString(7)
                });
            }

            return events;
        }

        #endregion
    }
}