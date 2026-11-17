namespace AsmodayCat.UI.Localization;

public static partial class Strings
{
    private static void Models()
    {
        A("models.title", "Моделі",
            "Models",
            "Модели");
        A("models.subtitle", "Локальні моделі Ollama: що встановлено, що завантажується, що зараз у пам’яті",
            "Local Ollama models: what is installed, downloading and loaded right now",
            "Локальные модели Ollama: что установлено, что загружается, что сейчас в памяти");
        A("models.installRecTip", "Завантажити моделі для чату, коду й зображень, яких ще немає",
            "Download the chat, code and image models that are missing",
            "Загрузить модели для чата, кода и изображений, которых ещё нет");
        A("models.installRec", "Встановити рекомендовані",
            "Install recommended",
            "Установить рекомендуемые");
        A("models.customHint", "назва-моделі:тег, напр. llama3.2:3b",
            "model-name:tag, e.g. llama3.2:3b",
            "имя-модели:тег, напр. llama3.2:3b");
        A("models.download", "Завантажити",
            "Download",
            "Загрузить");
        A("models.pool", "Набір моделей",
            "Model set",
            "Набор моделей");
        A("models.colModel", "Модель",
            "Model",
            "Модель");
        A("models.colTask", "Призначення",
            "Purpose",
            "Назначение");
        A("models.colState", "Стан",
            "State",
            "Состояние");
        A("models.colActions", "Дії",
            "Actions",
            "Действия");
        A("models.cancelTip", "Скасувати завантаження (продовжиться з того ж місця наступного разу)",
            "Cancel the download (it resumes from the same point next time)",
            "Отменить загрузку (продолжится с того же места в следующий раз)");
        A("models.unloadTip", "Вивантажити з відеопам’яті",
            "Unload from video memory",
            "Выгрузить из видеопамяти");
        A("models.deleteTip", "Видалити з диска",
            "Delete from disk",
            "Удалить с диска");
        A("models.sdTip", "Stable Diffusion — не модель Ollama. Запустіть AUTOMATIC1111/Forge WebUI з параметром --api на порту 7860, щоб увімкнути генерацію зображень.",
            "Stable Diffusion is not an Ollama model. Run AUTOMATIC1111/Forge WebUI with --api on port 7860 to enable image generation.",
            "Stable Diffusion — не модель Ollama. Запустите AUTOMATIC1111/Forge WebUI с параметром --api на порту 7860, чтобы включить генерацию изображений.");
        A("models.viaWebUi", "через WebUI",
            "via WebUI",
            "через WebUI");
        A("models.refreshing", "Оновлюю…",
            "Refreshing…",
            "Обновляю…");
        A("models.serviceDown", "Служба недоступна",
            "The service is unavailable",
            "Служба недоступна");
        A("models.summary", "Моделей: {0} • готових: {1}",
            "Models: {0} • ready: {1}",
            "Моделей: {0} • готовых: {1}");
        A("models.confirmDelete", "Видалити {0} з диска?\nЇї можна буде завантажити знову.",
            "Delete {0} from disk?\nYou can download it again later.",
            "Удалить {0} с диска?\nЕё можно будет загрузить снова.");
        A("models.confirmDeleteTitle", "AsmodayCat — видалення моделі",
            "AsmodayCat — delete model",
            "AsmodayCat — удаление модели");
        A("models.deleting", "Видаляю {0}…",
            "Deleting {0}…",
            "Удаляю {0}…");
        A("models.deleted", "{0} видалено",
            "{0} deleted",
            "{0} удалена");
        A("models.deleteFailed", "Не вдалося видалити: {0}",
            "Could not delete: {0}",
            "Не удалось удалить: {0}");
        A("models.downloading", "Завантажую {0}…",
            "Downloading {0}…",
            "Загружаю {0}…");
        A("models.unloading", "Вивантажую {0}…",
            "Unloading {0}…",
            "Выгружаю {0}…");
        A("models.unloaded", "Модель вивантажено",
            "Model unloaded",
            "Модель выгружена");
        A("models.unloadFailed", "Не вдалося вивантажити: {0}",
            "Could not unload: {0}",
            "Не удалось выгрузить: {0}");
        A("models.ready", "{0} готова",
            "{0} is ready",
            "{0} готова");
        A("models.cancelled", "{0}: завантаження скасовано",
            "{0}: download cancelled",
            "{0}: загрузка отменена");
        A("models.cancelling", "Скасовую {0}…",
            "Cancelling {0}…",
            "Отменяю {0}…");
        A("models.allInstalled", "Усі рекомендовані моделі вже встановлено",
            "All recommended models are already installed",
            "Все рекомендуемые модели уже установлены");
        A("models.downloadingList", "Завантажую: {0}",
            "Downloading: {0}",
            "Загружаю: {0}");
    }
}
