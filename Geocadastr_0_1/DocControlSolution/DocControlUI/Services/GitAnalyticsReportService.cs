using ClosedXML.Excel;
using DocControlService.Shared;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using OpenXmlWord = DocumentFormat.OpenXml.Wordprocessing;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DocControlUI.Services
{
    /// <summary>
    /// Сервіс для генерації розширених аналітичних звітів на основі git-історії директорії.
    /// Підтримує формати Excel (.xlsx) та Word (.docx).
    /// </summary>
    public class GitAnalyticsReportService
    {
        // ──────────────────────────────────────────────────────────────────────────
        // EXCEL
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Генерує Excel-звіт з двома аркушами:
        /// "Журнал подій"  — рядок на кожен змінений файл у кожному коміті;
        /// "Статистика"    — хто зробив найбільше змін та які файли редагуються найчастіше.
        /// </summary>
        /// <param name="history">Список комітів з переліком змінених файлів</param>
        /// <param name="filePath">Шлях до вихідного .xlsx файлу</param>
        /// <param name="directoryName">Назва директорії для заголовку звіту</param>
        public void GenerateExcelReport(
            List<GitCommitHistoryModel> history,
            string filePath,
            string directoryName)
        {
            using var workbook = new XLWorkbook();

            BuildEventLogSheet(workbook, history, directoryName);
            BuildStatisticsSheet(workbook, history, directoryName);

            workbook.SaveAs(filePath);
        }

        /// <summary>
        /// Будує аркуш "Журнал подій" — детальний перелік кожної зміни файлу.
        /// </summary>
        private void BuildEventLogSheet(
            XLWorkbook workbook,
            List<GitCommitHistoryModel> history,
            string directoryName)
        {
            var ws = workbook.Worksheets.Add("Журнал подій");

            // Заголовок звіту
            ws.Cell(1, 1).Value = $"Журнал змін: {directoryName}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 6).Merge();

            ws.Cell(2, 1).Value = $"Дата формування: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            ws.Range(2, 1, 2, 6).Merge();

            // Заголовки стовпців
            int headerRow = 4;
            ws.Cell(headerRow, 1).Value = "Дата";
            ws.Cell(headerRow, 2).Value = "Автор";
            ws.Cell(headerRow, 3).Value = "Хеш коміту";
            ws.Cell(headerRow, 4).Value = "Повідомлення";
            ws.Cell(headerRow, 5).Value = "Файл";
            ws.Cell(headerRow, 6).Value = "Тип зміни";

            var headerRange = ws.Range(headerRow, 1, headerRow, 6);
            headerRange.Style.Font.Bold = true;
            headerRange.Style.Fill.BackgroundColor = XLColor.SteelBlue;
            headerRange.Style.Font.FontColor = XLColor.White;
            headerRange.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Рядки даних — один рядок на файл у коміті
            int row = headerRow + 1;
            foreach (var commit in history)
            {
                if (commit.ChangedFiles == null || commit.ChangedFiles.Count == 0)
                {
                    // Коміт без змін файлів (наприклад, merge) — виводимо один порожній рядок
                    FillEventRow(ws, row, commit, "(немає змін)", string.Empty);
                    row++;
                    continue;
                }

                foreach (var file in commit.ChangedFiles)
                {
                    FillEventRow(ws, row, commit, file.FilePath, file.ChangeType);

                    // Колірне кодування типу зміни
                    ws.Cell(row, 6).Style.Fill.BackgroundColor = GetChangeTypeColor(file.ChangeType);

                    row++;
                }
            }

            ws.Columns().AdjustToContents();

            // Фіксуємо рядок заголовків при прокрутці
            ws.SheetView.FreezeRows(headerRow);
        }

        /// <summary>Заповнює один рядок журналу подій.</summary>
        private static void FillEventRow(
            IXLWorksheet ws, int row,
            GitCommitHistoryModel commit,
            string filePath, string changeType)
        {
            ws.Cell(row, 1).Value = commit.Date.ToString("yyyy-MM-dd HH:mm:ss");
            ws.Cell(row, 2).Value = commit.Author;
            ws.Cell(row, 3).Value = commit.Hash?.Length > 7 ? commit.Hash.Substring(0, 7) : commit.Hash;
            ws.Cell(row, 4).Value = commit.Message;
            ws.Cell(row, 5).Value = filePath;
            ws.Cell(row, 6).Value = changeType;
        }

        /// <summary>Повертає колір фону залежно від типу зміни.</summary>
        private static XLColor GetChangeTypeColor(string changeType) =>
            changeType switch
            {
                "Додано"       => XLColor.LightGreen,
                "Видалено"     => XLColor.LightCoral,
                "Змінено"      => XLColor.LightYellow,
                "Перейменовано"=> XLColor.LightBlue,
                _              => XLColor.White
            };

        /// <summary>
        /// Будує аркуш "Статистика": хто зробив найбільше змін та які файли редагуються найчастіше.
        /// </summary>
        private void BuildStatisticsSheet(
            XLWorkbook workbook,
            List<GitCommitHistoryModel> history,
            string directoryName)
        {
            var ws = workbook.Worksheets.Add("Статистика");

            ws.Cell(1, 1).Value = $"Статистика активності: {directoryName}";
            ws.Cell(1, 1).Style.Font.Bold = true;
            ws.Cell(1, 1).Style.Font.FontSize = 14;
            ws.Range(1, 1, 1, 3).Merge();

            ws.Cell(2, 1).Value = $"Дата формування: {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
            ws.Range(2, 1, 2, 3).Merge();

            // ── Активність авторів ──────────────────────────────────────────────
            int row = 4;
            ws.Cell(row, 1).Value = "Активність авторів (за кількістю файлових змін)";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.SteelBlue;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
            ws.Range(row, 1, row, 3).Merge();
            row++;

            ws.Cell(row, 1).Value = "Автор";
            ws.Cell(row, 2).Value = "Кількість комітів";
            ws.Cell(row, 3).Value = "Кількість змін файлів";
            ws.Range(row, 1, row, 3).Style.Font.Bold = true;
            ws.Range(row, 1, row, 3).Style.Fill.BackgroundColor = XLColor.LightBlue;
            row++;

            var authorStats = history
                .GroupBy(c => c.Author ?? "Невідомо")
                .Select(g => new
                {
                    Author    = g.Key,
                    Commits   = g.Count(),
                    FileChanges = g.Sum(c => c.ChangedFiles?.Count ?? 0)
                })
                .OrderByDescending(a => a.FileChanges)
                .ToList();

            foreach (var stat in authorStats)
            {
                ws.Cell(row, 1).Value = stat.Author;
                ws.Cell(row, 2).Value = stat.Commits;
                ws.Cell(row, 3).Value = stat.FileChanges;
                row++;
            }

            // ── Найбільш змінювані файли ────────────────────────────────────────
            row += 2;
            ws.Cell(row, 1).Value = "Найбільш змінювані файли";
            ws.Cell(row, 1).Style.Font.Bold = true;
            ws.Cell(row, 1).Style.Fill.BackgroundColor = XLColor.SteelBlue;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.White;
            ws.Range(row, 1, row, 3).Merge();
            row++;

            ws.Cell(row, 1).Value = "Файл";
            ws.Cell(row, 2).Value = "Кількість змін";
            ws.Cell(row, 3).Value = "Типи змін";
            ws.Range(row, 1, row, 3).Style.Font.Bold = true;
            ws.Range(row, 1, row, 3).Style.Fill.BackgroundColor = XLColor.LightBlue;
            row++;

            var fileStats = history
                .SelectMany(c => c.ChangedFiles ?? new List<GitFileChangeModel>())
                .GroupBy(f => f.FilePath)
                .Select(g => new
                {
                    File        = g.Key,
                    ChangeCount = g.Count(),
                    Types       = string.Join(", ", g.Select(f => f.ChangeType).Distinct())
                })
                .OrderByDescending(f => f.ChangeCount)
                .Take(50)
                .ToList();

            foreach (var stat in fileStats)
            {
                ws.Cell(row, 1).Value = stat.File;
                ws.Cell(row, 2).Value = stat.ChangeCount;
                ws.Cell(row, 3).Value = stat.Types;
                row++;
            }

            ws.Columns().AdjustToContents();
        }

        // ──────────────────────────────────────────────────────────────────────────
        // WORD
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Генерує Word-звіт зі структурованим аналізом активності:
        /// – текстовий аналіз (кількість змін, файлів, активний автор);
        /// – таблиця комітів зі зміненими файлами;
        /// – статистична секція.
        /// </summary>
        /// <param name="history">Список комітів з переліком змінених файлів</param>
        /// <param name="filePath">Шлях до вихідного .docx файлу</param>
        /// <param name="directoryName">Назва директорії для заголовку звіту</param>
        public void GenerateWordReport(
            List<GitCommitHistoryModel> history,
            string filePath,
            string directoryName)
        {
            using var doc = WordprocessingDocument.Create(filePath, WordprocessingDocumentType.Document);

            var mainPart = doc.AddMainDocumentPart();
            mainPart.Document = new OpenXmlWord.Document();
            var body = mainPart.Document.AppendChild(new OpenXmlWord.Body());

            AddWordTitle(body, $"Звіт активності: {directoryName}");
            AddWordParagraph(body, $"Дата формування: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            AddWordSpacer(body);

            // ── Текстовий аналіз ────────────────────────────────────────────────
            AddWordHeading(body, "Зведений аналіз");

            int totalCommits  = history.Count;
            int totalChanges  = history.Sum(c => c.ChangedFiles?.Count ?? 0);
            int uniqueFiles   = history
                .SelectMany(c => c.ChangedFiles ?? new List<GitFileChangeModel>())
                .Select(f => f.FilePath)
                .Distinct()
                .Count();

            var mostActiveAuthor = history
                .GroupBy(c => c.Author ?? "Невідомо")
                .OrderByDescending(g => g.Sum(c => c.ChangedFiles?.Count ?? 0))
                .FirstOrDefault();

            var mostChangedFile = history
                .SelectMany(c => c.ChangedFiles ?? new List<GitFileChangeModel>())
                .GroupBy(f => f.FilePath)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();

            AddWordParagraph(body,
                $"За звітний період було зафіксовано {totalCommits} комітів із {totalChanges} змінами " +
                $"у {uniqueFiles} унікальних файлах.");

            if (mostActiveAuthor != null)
            {
                int authorChanges = mostActiveAuthor.Sum(c => c.ChangedFiles?.Count ?? 0);
                AddWordParagraph(body,
                    $"Найактивнішим учасником є \"{mostActiveAuthor.Key}\" " +
                    $"({mostActiveAuthor.Count()} комітів, {authorChanges} змін файлів).");
            }

            if (mostChangedFile != null)
                AddWordParagraph(body,
                    $"Файл, що найчастіше редагувався: \"{mostChangedFile.Key}\" " +
                    $"({mostChangedFile.Count()} змін).");

            AddWordSpacer(body);

            // ── Таблиця комітів ─────────────────────────────────────────────────
            AddWordHeading(body, "Журнал змін");
            BuildWordCommitsTable(body, history);
            AddWordSpacer(body);

            // ── Статистика авторів ──────────────────────────────────────────────
            AddWordHeading(body, "Активність авторів");
            BuildWordAuthorStatsTable(body, history);
            AddWordSpacer(body);

            // ── Топ змінюваних файлів ───────────────────────────────────────────
            AddWordHeading(body, "Найбільш змінювані файли");
            BuildWordFileStatsTable(body, history);

            mainPart.Document.Save();
        }

        /// <summary>Будує таблицю комітів у Word-документі.</summary>
        private void BuildWordCommitsTable(OpenXmlWord.Body body, List<GitCommitHistoryModel> history)
        {
            var table = CreateWordTable();
            AppendWordTableRow(table,
                new[] { "Дата", "Автор", "Хеш", "Повідомлення", "Файл", "Тип зміни" },
                isHeader: true);

            foreach (var commit in history)
            {
                if (commit.ChangedFiles == null || commit.ChangedFiles.Count == 0)
                {
                    AppendWordTableRow(table,
                        new[] {
                            commit.Date.ToString("yyyy-MM-dd HH:mm"),
                            commit.Author ?? string.Empty,
                            commit.Hash?.Length > 7 ? commit.Hash.Substring(0, 7) : commit.Hash,
                            commit.Message ?? string.Empty,
                            "(немає змін)",
                            string.Empty
                        });
                    continue;
                }

                foreach (var file in commit.ChangedFiles)
                {
                    AppendWordTableRow(table,
                        new[] {
                            commit.Date.ToString("yyyy-MM-dd HH:mm"),
                            commit.Author ?? string.Empty,
                            commit.Hash?.Length > 7 ? commit.Hash.Substring(0, 7) : commit.Hash,
                            commit.Message ?? string.Empty,
                            file.FilePath ?? string.Empty,
                            file.ChangeType ?? string.Empty
                        });
                }
            }

            body.Append(table);
        }

        /// <summary>Будує таблицю статистики авторів у Word-документі.</summary>
        private void BuildWordAuthorStatsTable(OpenXmlWord.Body body, List<GitCommitHistoryModel> history)
        {
            var table = CreateWordTable();
            AppendWordTableRow(table,
                new[] { "Автор", "Кількість комітів", "Змін файлів" },
                isHeader: true);

            var stats = history
                .GroupBy(c => c.Author ?? "Невідомо")
                .Select(g => new {
                    Author      = g.Key,
                    Commits     = g.Count(),
                    FileChanges = g.Sum(c => c.ChangedFiles?.Count ?? 0)
                })
                .OrderByDescending(a => a.FileChanges);

            foreach (var s in stats)
                AppendWordTableRow(table,
                    new[] { s.Author, s.Commits.ToString(), s.FileChanges.ToString() });

            body.Append(table);
        }

        /// <summary>Будує таблицю найбільш змінюваних файлів у Word-документі.</summary>
        private void BuildWordFileStatsTable(OpenXmlWord.Body body, List<GitCommitHistoryModel> history)
        {
            var table = CreateWordTable();
            AppendWordTableRow(table,
                new[] { "Файл", "Кількість змін", "Типи змін" },
                isHeader: true);

            var stats = history
                .SelectMany(c => c.ChangedFiles ?? new List<GitFileChangeModel>())
                .GroupBy(f => f.FilePath)
                .Select(g => new {
                    File        = g.Key,
                    ChangeCount = g.Count(),
                    Types       = string.Join(", ", g.Select(f => f.ChangeType).Distinct())
                })
                .OrderByDescending(f => f.ChangeCount)
                .Take(50);

            foreach (var s in stats)
                AppendWordTableRow(table,
                    new[] { s.File, s.ChangeCount.ToString(), s.Types });

            body.Append(table);
        }

        // ──────────────────────────────────────────────────────────────────────────
        // Word допоміжні методи
        // ──────────────────────────────────────────────────────────────────────────

        /// <summary>Додає жирний великий заголовок документу.</summary>
        private static void AddWordTitle(OpenXmlWord.Body body, string text)
        {
            var p    = body.AppendChild(new OpenXmlWord.Paragraph());
            var run  = p.AppendChild(new OpenXmlWord.Run());
            run.AppendChild(new OpenXmlWord.Text(text));
            var props = run.AppendChild(new OpenXmlWord.RunProperties());
            props.AppendChild(new OpenXmlWord.Bold());
            props.AppendChild(new OpenXmlWord.FontSize { Val = "36" });
        }

        /// <summary>Додає заголовок розділу.</summary>
        private static void AddWordHeading(OpenXmlWord.Body body, string text)
        {
            var p    = body.AppendChild(new OpenXmlWord.Paragraph());
            var run  = p.AppendChild(new OpenXmlWord.Run());
            run.AppendChild(new OpenXmlWord.Text(text));
            var props = run.AppendChild(new OpenXmlWord.RunProperties());
            props.AppendChild(new OpenXmlWord.Bold());
            props.AppendChild(new OpenXmlWord.FontSize { Val = "28" });
        }

        /// <summary>Додає звичайний абзац тексту.</summary>
        private static void AddWordParagraph(OpenXmlWord.Body body, string text)
        {
            var p = body.AppendChild(new OpenXmlWord.Paragraph());
            p.AppendChild(new OpenXmlWord.Run(new OpenXmlWord.Text(text)));
        }

        /// <summary>Додає порожній рядок (відступ між секціями).</summary>
        private static void AddWordSpacer(OpenXmlWord.Body body) =>
            body.AppendChild(new OpenXmlWord.Paragraph());

        /// <summary>Створює таблицю з рамками.</summary>
        private static OpenXmlWord.Table CreateWordTable()
        {
            var table = new OpenXmlWord.Table();
            var borders = new OpenXmlWord.TableBorders(
                new OpenXmlWord.TopBorder    { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 6 },
                new OpenXmlWord.BottomBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 6 },
                new OpenXmlWord.LeftBorder   { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 6 },
                new OpenXmlWord.RightBorder  { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 6 },
                new OpenXmlWord.InsideHorizontalBorder { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 6 },
                new OpenXmlWord.InsideVerticalBorder   { Val = new EnumValue<OpenXmlWord.BorderValues>(OpenXmlWord.BorderValues.Single), Size = 6 }
            );
            table.AppendChild(new OpenXmlWord.TableProperties(borders));
            return table;
        }

        /// <summary>Додає рядок до таблиці. Заголовний рядок виводиться жирним.</summary>
        private static void AppendWordTableRow(
            OpenXmlWord.Table table,
            string[] cells,
            bool isHeader = false)
        {
            var row = new OpenXmlWord.TableRow();
            foreach (var cellText in cells)
            {
                var cell      = new OpenXmlWord.TableCell();
                var paragraph = new OpenXmlWord.Paragraph();
                var run       = new OpenXmlWord.Run(new OpenXmlWord.Text(cellText ?? string.Empty));

                if (isHeader)
                {
                    var runProps = new OpenXmlWord.RunProperties();
                    runProps.AppendChild(new OpenXmlWord.Bold());
                    run.PrependChild(runProps);
                }

                paragraph.Append(run);
                cell.Append(paragraph);
                row.Append(cell);
            }
            table.Append(row);
        }
    }
}
