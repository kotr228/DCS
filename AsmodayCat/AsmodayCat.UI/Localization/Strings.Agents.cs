namespace AsmodayCat.UI.Localization;

public static partial class Strings
{
    private static void Agents()
    {
        A("agents.title", "Агенти в папках",
            "Folder agents",
            "Агенты в папках");
        A("agents.subtitle", "Покладіть файл у теку — агент сам його обробить і збереже результат",
            "Drop a file into a folder — the agent processes it and saves the result",
            "Положите файл в папку — агент сам его обработает и сохранит результат");
        A("agents.list", "Теки агентів",
            "Agent folders",
            "Папки агентов");
        A("agents.emptyTitle", "Ще немає жодної теки",
            "No folders yet",
            "Пока нет ни одной папки");
        A("agents.emptyText", "Додайте теку у формі праворуч: агент перекладатиме, підсумовуватиме чи описуватиме все, що в ній з’явиться.",
            "Add a folder with the form on the right: the agent will translate, summarize or describe everything that appears in it.",
            "Добавьте папку в форме справа: агент будет переводить, кратко излагать или описывать всё, что в ней появится.");
        A("agents.internetChip", "інтернет",
            "internet",
            "интернет");
        A("agents.resultsTo", "Результати → ",
            "Results → ",
            "Результаты → ");
        A("agents.toggleTip", "Стежить / на паузі",
            "Watching / paused",
            "Следит / на паузе");
        A("agents.reprocessTip", "Обробити всі файли в теці заново",
            "Process every file in the folder again",
            "Обработать все файлы в папке заново");
        A("agents.editTip", "Змінити правило",
            "Edit the rule",
            "Изменить правило");
        A("agents.removeTip", "Зупинити й видалити правило",
            "Stop and delete the rule",
            "Остановить и удалить правило");
        A("agents.formTitle", "Нова тека для агента",
            "New folder for an agent",
            "Новая папка для агента");
        A("agents.folderHint", "Тека, за якою стежити (напр. C:\\CatSuite\\Tasks)",
            "Folder to watch (e.g. C:\\CatSuite\\Tasks)",
            "Папка для наблюдения (напр. C:\\CatSuite\\Tasks)");
        A("agents.outputHint", "Тека для результатів (необов’язково)",
            "Results folder (optional)",
            "Папка для результатов (необязательно)");
        A("agents.outputTip", "Порожньо — результати з’являться в підтеці AsmodayCat_Output",
            "Empty — results go to the AsmodayCat_Output subfolder",
            "Пусто — результаты появятся в подпапке AsmodayCat_Output");
        A("agents.actionHint", "Що робити з файлами",
            "What to do with files",
            "Что делать с файлами");
        A("agents.promptHint", "Власні інструкції для агента (необов’язково)",
            "Your own instructions for the agent (optional)",
            "Собственные инструкции для агента (необязательно)");
        A("agents.extHint", "Типи файлів (*.*  або  .txt,.md,.pdf)",
            "File types (*.*  or  .txt,.md,.pdf)",
            "Типы файлов (*.*  или  .txt,.md,.pdf)");
        A("agents.internetTip", "Агент зможе шукати в інтернеті й читати сторінки",
            "The agent can search the internet and read pages",
            "Агент сможет искать в интернете и читать страницы");
        A("agents.internet", "Дозволити агенту інтернет",
            "Allow the agent internet access",
            "Разрешить агенту интернет");
        A("agents.existingTip", "Інакше агент братиме лише файли, що з’являться після додавання",
            "Otherwise the agent only takes files that appear after adding",
            "Иначе агент будет брать только файлы, появившиеся после добавления");
        A("agents.existing", "Обробити й файли, що вже лежать у теці",
            "Also process files already in the folder",
            "Обработать и файлы, которые уже лежат в папке");
        A("agents.saveChanges", "Зберегти зміни",
            "Save changes",
            "Сохранить изменения");
        A("agents.add", "Додати теку",
            "Add folder",
            "Добавить папку");
        A("agents.editing", "Редагування правила — змініть поля й натисніть «Зберегти зміни».",
            "Editing the rule — change the fields and press “Save changes”.",
            "Редактирование правила — измените поля и нажмите «Сохранить изменения».");
        A("agents.needFolder", "Вкажіть теку, за якою стежити.",
            "Choose a folder to watch.",
            "Укажите папку для наблюдения.");
        A("agents.added", "Теку додано, агент стежить за нею. Результати — у теці результатів (типово AsmodayCat_Output усередині).",
            "Folder added; the agent is watching it. Results go to the results folder (AsmodayCat_Output inside by default).",
            "Папка добавлена, агент следит за ней. Результаты — в папке результатов (по умолчанию AsmodayCat_Output внутри).");
        A("agents.updated", "Зміни збережено, агент працює з новими налаштуваннями.",
            "Changes saved; the agent uses the new settings.",
            "Изменения сохранены, агент работает с новыми настройками.");
        A("agents.confirmReprocess", "Обробити заново всі файли в теці\n{0}?\nНаявні результати буде перезаписано.",
            "Process every file in the folder again?\n{0}\nExisting results will be overwritten.",
            "Обработать заново все файлы в папке\n{0}?\nИмеющиеся результаты будут перезаписаны.");
        A("agents.confirmTitle", "AsmodayCat — агент",
            "AsmodayCat — agent",
            "AsmodayCat — агент");
        A("agents.queued", "У черзі файлів: {0}",
            "Files queued: {0}",
            "Файлов в очереди: {0}");
        A("agents.resumed", "Агент знову стежить.",
            "The agent is watching again.",
            "Агент снова следит.");
        A("agents.paused", "Агент на паузі.",
            "The agent is paused.",
            "Агент на паузе.");
        A("agents.removed", "Теку видалено.",
            "Folder removed.",
            "Папка удалена.");
    }
}
