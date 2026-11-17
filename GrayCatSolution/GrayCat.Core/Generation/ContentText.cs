namespace GrayCat.Core.Generation;

/// <summary>
/// Starter content (block defaults, templates, texts the generator adds) in the site's language.
/// Keys are the English texts; languages without an entry use English.
/// </summary>
public static class ContentText
{
    private static readonly Dictionary<string, string> Uk = new(StringComparer.Ordinal)
    {
        // Block defaults
        ["Write your text here."] = "Напишіть тут свій текст.",
        ["Welcome"] = "Вітаємо",
        ["Home"] = "Головна",
        ["About"] = "Про нас",
        ["Contact"] = "Контакти",
        ["Brand"] = "Бренд",
        ["Welcome to Our Site"] = "Вітаємо на нашому сайті",
        ["Build amazing things together"] = "Створюймо чудові речі разом",
        ["Get Started"] = "Почати",
        ["Click Me"] = "Натисніть",
        ["Card Title"] = "Заголовок картки",
        ["Card description text goes here"] = "Тут буде опис картки",
        ["Learn More"] = "Докладніше",
        ["Image"] = "Зображення",
        ["Gallery"] = "Галерея",
        ["Photo 1"] = "Фото 1",
        ["Photo 2"] = "Фото 2",
        ["Photo 3"] = "Фото 3",
        ["Our Products"] = "Наші товари",
        ["View"] = "Переглянути",
        ["Product One"] = "Товар перший",
        ["Product Two"] = "Товар другий",
        ["Product Three"] = "Товар третій",
        ["Short product description"] = "Короткий опис товару",
        ["Subscribe"] = "Підписатися",
        ["Get the latest news straight to your inbox"] = "Отримуйте свіжі новини просто на пошту",
        ["Thanks for subscribing!"] = "Дякуємо за підписку!",
        ["Oops! Something went wrong. Please try again."] = "Ой! Щось пішло не так. Спробуйте ще раз.",
        ["Email"] = "Ел. пошта",
        ["Get in Touch"] = "Зв'яжіться з нами",
        ["We'd love to hear from you"] = "Будемо раді вас почути",
        ["Send Message"] = "Надіслати",
        ["Thank you! We'll get back to you soon."] = "Дякуємо! Ми скоро відповімо.",
        ["Name"] = "Ім'я",
        ["Message"] = "Повідомлення",
        ["This product changed my life!"] = "Цей продукт змінив моє життя!",
        ["John Doe"] = "Іван Петренко",
        ["CEO, Company"] = "Директор компанії",
        ["Pro Plan"] = "Професійний",
        ["month"] = "місяць",
        ["Feature 1"] = "Перевага 1",
        ["Feature 2"] = "Перевага 2",
        ["Feature 3"] = "Перевага 3",
        ["Frequently Asked Questions"] = "Поширені запитання",
        ["What is this?"] = "Що це таке?",
        ["This is an answer."] = "Це відповідь.",
        ["How does it work?"] = "Як це працює?",
        ["It works like this."] = "Ось так.",
        ["Happy Clients"] = "Задоволених клієнтів",
        ["Projects Completed"] = "Виконаних проєктів",
        ["Support"] = "Підтримка",
        ["Privacy"] = "Конфіденційність",
        ["Terms"] = "Умови",

        // Templates
        ["Contact us"] = "Зв'язатися",
        ["Features"] = "Переваги",
        ["Pricing"] = "Ціни",
        ["Welcome to {0}"] = "Вітаємо в {0}",
        ["Everything you need, in one place."] = "Усе потрібне — в одному місці.",
        ["See pricing"] = "Переглянути ціни",
        ["Most popular"] = "Найпопулярніший",
        ["About us"] = "Про нас",
        ["Services"] = "Послуги",
        ["Reliable services for growing businesses."] = "Надійні послуги для бізнесу, що росте.",
        ["Our services"] = "Наші послуги",
        ["Who we are and what we believe in."] = "Хто ми і в що віримо.",
        ["{0} was founded to help clients succeed. Tell your story here: how you started, what you do best and why people choose you."] =
            "{0} створено, щоб допомагати клієнтам досягати успіху. Розкажіть тут свою історію: як ви починали, що вмієте найкраще і чому обирають саме вас.",
        ["What we can do for you."] = "Що ми можемо для вас зробити.",
        ["Consulting"] = "Консалтинг",
        ["We look at your situation and suggest the best way forward."] = "Ми розбираємося у вашій ситуації й пропонуємо найкращий шлях.",
        ["Get in touch"] = "Зв'язатися",
        ["Implementation"] = "Впровадження",
        ["Our team puts the plan into practice, on time and on budget."] = "Наша команда втілює план вчасно і в межах бюджету.",
        ["We usually reply within one business day."] = "Зазвичай ми відповідаємо протягом одного робочого дня.",
        ["Work"] = "Роботи",
        ["Clients"] = "Клієнти",
        ["Hi, I'm {0}"] = "Привіт, я {0}",
        ["Designer and photographer. Here is some of my recent work."] = "Дизайнер і фотограф. Ось деякі з моїх останніх робіт.",
        ["View work"] = "Переглянути роботи",
        ["Selected work"] = "Вибрані роботи",
        ["Working together was a pleasure. The result exceeded our expectations."] = "Працювати разом було приємно. Результат перевершив наші очікування.",
        ["Happy Client"] = "Задоволений клієнт",
        ["Founder, Studio"] = "Засновниця студії",
        ["Let's work together"] = "Попрацюймо разом",
        ["Shop now"] = "До покупок",
        ["Products"] = "Товари",
        ["Membership"] = "Членство",
        ["FAQ"] = "Питання",
        ["Quality products, delivered to your door."] = "Якісні товари з доставкою до дверей.",
        ["Browse products"] = "Переглянути товари",
        ["Free delivery"] = "Безкоштовна доставка",
        ["Member prices"] = "Ціни для учасників",
        ["Early access to sales"] = "Ранній доступ до розпродажів",
        ["How long does delivery take?"] = "Скільки триває доставка?",
        ["Usually 2-4 business days."] = "Зазвичай 2–4 робочі дні.",
        ["Can I return an item?"] = "Чи можна повернути товар?",
        ["Yes, within 30 days of delivery."] = "Так, протягом 30 днів після доставки.",
        ["Get our newsletter"] = "Підпишіться на розсилку",

        // Added by the generator
        ["Phone"] = "Телефон",
        ["Subject"] = "Тема",
        ["Sending..."] = "Надсилання...",
        ["© {0} {1}. All rights reserved."] = "© {0} {1}. Усі права захищено.",
        ["{0} is required"] = "Заповніть поле «{0}»",
    };

    /// <summary>Normalises a site language to the one used for starter content ("uk" or "en").</summary>
    public static string LanguageOf(string? language) =>
        language != null && language.StartsWith("uk", StringComparison.OrdinalIgnoreCase) ? "uk" : "en";

    public static string T(string? language, string english) =>
        LanguageOf(language) == "uk" && Uk.TryGetValue(english, out var text) ? text : english;

    public static string F(string? language, string english, params object?[] args) =>
        string.Format(T(language, english), args);

    /// <summary>Like <see cref="Localize"/>, but also translates the texts inside JSON list values ("[...]").</summary>
    public static object? LocalizeEditable(string? language, object? value)
    {
        if (LanguageOf(language) == "en") return value;
        if (value is string s && s.TrimStart().StartsWith('['))
        {
            try
            {
                var plain = PropertyValues.ToPlain(s);
                return System.Text.Json.JsonSerializer.Serialize(Localize(language, plain),
                    new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            }
            catch (System.Text.Json.JsonException)
            {
                return value;
            }
        }
        return Localize(language, value);
    }

    /// <summary>Translates every text in a property value (strings, lists, objects); other values stay as they are.</summary>
    public static object? Localize(string? language, object? value) => value switch
    {
        string s => T(language, s),
        List<object?> list => list.Select(v => Localize(language, v)).ToList(),
        Dictionary<string, object?> map => map.ToDictionary(kv => kv.Key, kv => kv.Key is "url" or "src" or "image" or "platform" or "type" or "number"
            ? kv.Value
            : Localize(language, kv.Value)),
        _ => value
    };
}
