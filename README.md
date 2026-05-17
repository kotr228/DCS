# 🐱 DCS — Document Control System

**DCS** — репозиторій проектів для захищеного документообігу та мережевої комунікації між вузлами підприємства.

## 📦 Склад репозиторію

```
DCS/
├── BlackCat/              # 🐱 Брандмауер з MQE-шифруванням та DCS-інтеграцією
├── GrayCatSolution/       # 🐈 Допоміжна система документообігу
├── CatSuite/              # 📦 Пакет утиліт (лаунчер, інсталятор)
├── Geocadastr_0_1/        # 🗺️ Геокадастр / CoffeeCat (DocControlSolution)
└── Лабораторні роботи/    # 📖 Навчальні матеріали
```

## 🔗 Два ключові проекти

### BlackCat
Брандмауер нового покоління з вбудованим зашифрованим P2P-тунелем. Два вузли BlackCat з'єднуються напряму через NAT (UDP hole punching) і обмінюються трафіком, зашифрованим власним алгоритмом MQE (Modular Quaternion Encryption). Кожен вузол отримує унікальний **Black-ID** на основі hardware fingerprint. BlackCat також інтегрується з DCS: приймає команди від CoffeeCat через named pipe `BlackCatCommandPipe` і передає цілі директорії між вузлами через MQE-тунель.

📄 Документація: [`BlackCat/README.md`](BlackCat/README.md) · [`BlackCat/ARCHITECTURE.md`](BlackCat/ARCHITECTURE.md) · [`BlackCat/SUMMARY.md`](BlackCat/SUMMARY.md)

### DCS / CoffeeCat (DocControlSolution)
Система документообігу та управління файлами підприємства. Дозволяє переглядати, надавати доступ та зеркалювати директорії між пристроями мережі. Взаємодіє з BlackCat-тунелем для захищеної передачі файлів. Інтерфейс побудований на WPF з кастомним chrome-оформленням без системної рамки.

📄 Документація: [`Geocadastr_0_1/DocControlSolution/BUILD.md`](Geocadastr_0_1/DocControlSolution/BUILD.md) · [`Geocadastr_0_1/DocControlSolution/NETWORK_TESTING.md`](Geocadastr_0_1/DocControlSolution/NETWORK_TESTING.md)

## 📚 Повна документація

👉 [`DOCUMENTATION.md`](DOCUMENTATION.md) — головний документ з описом архітектури, протоколів, бази даних та API.

---

**Версія:** 1.1.0 | **Оновлено:** 17.05.2026 | **Репозиторій:** https://github.com/kotr228/DCS
