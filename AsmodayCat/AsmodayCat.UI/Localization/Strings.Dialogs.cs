namespace AsmodayCat.UI.Localization;

public static partial class Strings
{
    private static void Dialogs()
    {
        // Approval pop-up
        A("approval.windowTitle", "AsmodayCat — потрібне підтвердження", "AsmodayCat — approval needed", "AsmodayCat — нужно подтверждение");
        A("approval.header", "Потрібне ваше підтвердження", "Your approval is needed", "Нужно ваше подтверждение");
        A("approval.source", "Хто просить: {0}", "Requested by: {0}", "Кто просит: {0}");
        A("approval.deny", "Відхилити", "Deny", "Отклонить");
        A("approval.approve", "Дозволити", "Allow", "Разрешить");
        A("approval.countdown", "Автоматична відмова через {0} с", "Denied automatically in {0} s", "Автоматический отказ через {0} с");
        A("approval.listen", "Слухаю… скажіть «так» / «дозволяю» або «ні» / «скасуй».",
                             "Listening… say “yes” / “approve” or “no” / “deny”.",
                             "Слушаю… скажите «да» / «разрешаю» или «нет» / «отмена».");
        A("approval.listenEnglish", "Слухаю… скажіть англійською «approve» / «confirm» (дозволити) або «no» / «deny» (відхилити).",
                                    "Listening… say “approve” / “confirm” or “no” / “deny”.",
                                    "Слушаю… скажите по-английски «approve» / «confirm» (разрешить) или «no» / «deny» (отклонить).");
        A("approval.heard", "Почуто: «{0}»", "Heard: “{0}”", "Услышано: «{0}»");
        A("approval.notUnderstood", "Почуто «{0}» — не зрозумів. Скажіть «так» або «ні».",
                                    "Heard “{0}” — didn't get it. Say “yes” or “no”.",
                                    "Услышано «{0}» — не понял. Скажите «да» или «нет».");
        A("approval.noMic", "Голосове підтвердження недоступне (немає мікрофона). Скористайтеся кнопками.",
                            "Voice approval is unavailable (no microphone). Use the buttons.",
                            "Голосовое подтверждение недоступно (нет микрофона). Используйте кнопки.");
        A("approval.voiceFailed", "Голос недоступний ({0}). Скористайтеся кнопками.",
                                  "Voice is unavailable ({0}). Use the buttons.",
                                  "Голос недоступен ({0}). Используйте кнопки.");

        // Quick question (Ctrl+Space)
        A("overlay.title", "Швидке питання", "Quick question", "Быстрый вопрос");
        A("overlay.escHint", "Esc — закрити", "Esc — close", "Esc — закрыть");
        A("overlay.hint", "Запитайте що завгодно…", "Ask anything…", "Спросите что угодно…");
        A("overlay.ask", "Запитати", "Ask", "Спросить");
        A("overlay.thinking", "Думаю…", "Thinking…", "Думаю…");
    }

    private static void Tray()
    {
        A("tray.open", "Відкрити AsmodayCat", "Open AsmodayCat", "Открыть AsmodayCat");
        A("tray.talk", "Говорити (Ctrl+Shift+Space)", "Talk (Ctrl+Shift+Space)", "Говорить (Ctrl+Shift+Space)");
        A("tray.restart", "Перезапустити службу", "Restart the service", "Перезапустить службу");
        A("tray.restarted", "Службу перезапущено.", "The service was restarted.", "Служба перезапущена.");
        A("tray.restartFailed", "Служба не запустилася — див. журнал.", "The service did not start — see the log.", "Служба не запустилась — см. журнал.");
        A("tray.hiddenTitle", "AsmodayCat працює у треї", "AsmodayCat is running in the tray", "AsmodayCat работает в трее");
        A("tray.hiddenText", "Агенти й гаряча клавіша Ctrl+Shift+Space працюють далі. Вийти — правою кнопкою на іконці.",
                             "Agents and the Ctrl+Shift+Space hotkey keep working. To exit, right-click the icon.",
                             "Агенты и горячая клавиша Ctrl+Shift+Space продолжают работать. Выйти — правой кнопкой по значку.");
        A("tray.watchdogOk", "Служба зупинилася і була запущена знову.", "The service stopped and was started again.", "Служба остановилась и была запущена снова.");
        A("tray.watchdogFail", "Служба не відповідає — див. журнал у теці logs.", "The service is not responding — see the log in the logs folder.", "Служба не отвечает — см. журнал в папке logs.");
    }

    private static void Voice()
    {
        A("voice.what.whisper", "модель розпізнавання мовлення", "speech recognition model", "модель распознавания речи");
        A("voice.what.engine", "голосовий рушій", "voice engine", "голосовой движок");
        A("voice.what.voice", "голос", "voice", "голос");
        A("voice.codeOnScreen", "(код показано на екрані)", "(the code is on the screen)", "(код показан на экране)");
        A("voice.restOnScreen", "Далі — на екрані.", "The rest is on the screen.", "Дальше — на экране.");
        A("voice.listening", "Слухаю… (пауза — кінець)", "Listening… (pause to finish)", "Слушаю… (пауза — конец)");
        A("voice.recognizing", "Розпізнаю…", "Recognizing…", "Распознаю…");
    }
}
