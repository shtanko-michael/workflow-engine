# Migration to V2 API with Message Versioning

## Что было реализовано

### ✅ Phase 1: Backend - Database Layer

**Создан новый `ApplicationDbContext`** для работы с диалогами и сообщениями:

#### Entities:
- **DialogEntity** (`dialogs`) - диалоги с чатами
- **MessageEntity** (`messages`) - логические позиции сообщений в диалоге
- **MessageVersionEntity** (`message_versions`) - версии содержимого сообщений
- **DialogActivePathEntity** (`dialog_active_paths`) - текущий активный путь (ветка) диалога

#### Ключевые особенности схемы:

**Таблица `messages`:**
```sql
- id: уникальный ID логического сообщения
- parent_message_id: связь parent-child для построения дерева
- ancestor_ids: массив ID всех предков (оптимизация)
- total_versions: количество версий (инкрементируется при редактировании)
- role: user | assistant | system
```

**Таблица `message_versions`:**
```sql
- id: уникальный ID версии
- message_id: FK к message
- checkpoint_id: привязка к чекпоинту workflow
- content: текст сообщения
- version_order: lexorank для сортировки (избегаем UPDATE всех siblings)
```

**Таблица `dialog_active_paths`:**
```sql
- dialog_id: FK к dialog
- active_version_ids: массив ID версий текущего активного пути
```

### ✅ Phase 2: Repositories & Services

**Создано:**
- `IDialogRepository` / `DialogRepository` - работа с диалогами
- `IMessageRepository` / `MessageRepository` - работа с сообщениями и версиями
- `LexoRankGenerator` - генератор лексикографических рангов для версий
- `ChatWorkflowServiceNew` - новый сервис с поддержкой версионирования
- `DialogsControllerNew` - новый контроллер на `/api/v2/dialogs`
- `DtoMapper` - маппинг entities в DTOs

**Ключевые методы:**
- `GetDialogMessagesAsync()` - загрузка сообщений с версиями (2 запроса в БД)
- `CreateVersionAsync()` - создание новой версии при редактировании
- `BuildPathFromVersionAsync()` - построение пути от выбранной версии
- `EditMessageAsync()` - редактирование сообщения с созданием ветки

### ✅ Phase 3: Frontend Updates

**Обновлено:**
- Добавлены TypeScript типы для `MessageWithVersions`
- Добавлен state для `messagesV2` и `editingMessageId`
- Добавлен SignalR listener для `messagesUpdated`
- Добавлена функция `handleEditMessage()` для редактирования
- Добавлена функция `handleSwitchVersion()` для переключения версий
- Обновлён UI для показа версий со стрелочками ◄ ► и счётчиком `1/3`
- Добавлена кнопка "Edit" при hover на user сообщениях

### ✅ Phase 4: EF Core Migrations

- Создана первая миграция `InitialCreate` через `dotnet ef migrations add`
- Настроен `ApplicationDbContextFactory` для design-time
- Добавлен `MigrateAsync()` в `Program.cs` для автоматического применения миграций

## Архитектура

### Два независимых DbContext:

1. **CheckpointDbContext** (custom migrations)
   - Таблицы: `checkpoints`, `checkpoint_blobs`, `checkpoint_migrations`
   - Для persistence workflow-графов
   - Миграции через SQL скрипты в коде

2. **ApplicationDbContext** (EF Core migrations)
   - Таблицы: `dialogs`, `messages`, `message_versions`, `dialog_active_paths`
   - Для UI представления чатов
   - Миграции через `Add-Migration`

**Оба контекста** работают с одной БД PostgreSQL.

## Как работает ветвление

### Пример структуры:

```
AI: "Hello" (msg_1)
  |
  ├─ User: "Tell me about cats" (msg_2a) [VERSION 1]
  |   └─ AI: "Cats are mammals..." (msg_3a)
  |
  └─ User: "Tell me about dogs" (msg_2b) [VERSION 2]
      └─ AI: "Dogs are loyal..." (msg_3b)
```

### Данные в БД:

**messages:**
| id | parent_id | role | total_versions | ancestor_ids |
|---|---|---|---|---|
| msg_1 | null | assistant | 1 | [] |
| msg_2 | msg_1 | user | 2 | [msg_1] |
| msg_3a | msg_2 | assistant | 1 | [msg_1, msg_2] |
| msg_3b | msg_2 | assistant | 1 | [msg_1, msg_2] |

**message_versions:**
| id | message_id | content | version_order | checkpoint_id |
|---|---|---|---|---|
| ver_1 | msg_1 | "Hello" | a0 | checkpoint_1 |
| ver_2a | msg_2 | "Tell me about cats" | a0 | checkpoint_1 |
| ver_2b | msg_2 | "Tell me about dogs" | a1 | checkpoint_1 |
| ver_3a | msg_3a | "Cats are mammals..." | a0 | checkpoint_2 |
| ver_3b | msg_3b | "Dogs are loyal..." | a0 | checkpoint_5 |

**dialog_active_paths:**
| dialog_id | active_version_ids |
|---|---|
| dialog_123 | [ver_1, ver_2b, ver_3b] |

### UI показывает:

```
AI: Hello
User: Tell me about dogs    [2/2] ◄ ►  <- версия 2 из 2
AI: Dogs are loyal...
```

## Как запустить

### 1. Применить миграции:

```bash
cd WorkflowEngine.Tests.UI/backend
dotnet ef database update --context ApplicationDbContext
```

Или просто запустить приложение - миграции применятся автоматически через `MigrateAsync()`.

### 2. Включить V2 API:

Frontend уже настроен через `.env.development`:
```env
VITE_USE_V2_API=true
```

### 3. Запустить:

```bash
# Backend
cd WorkflowEngine.Tests.UI/backend
dotnet run

# Frontend
cd WorkflowEngine.Tests.UI/frontend
npm run dev
```

### 4. Протестировать:

1. Создайте новый диалог
2. Отправьте несколько сообщений
3. Наведите на user сообщение → нажмите "Edit"
4. Измените текст и нажмите "Save"
5. После генерации ответа AI увидите стрелочки ◄ ► и счётчик версий
6. Переключайтесь между версиями стрелочками

## Что изменилось в API

### Старый API (`/api/dialogs`):
- Сообщения в памяти (`InMemoryChatStore`)
- Нет версионирования
- Нет ветвления

### Новый API (`/api/v2/dialogs`):
- Сообщения в PostgreSQL
- Полная поддержка версионирования
- Ветвление через parent-child структуру
- Lexorank для эффективной сортировки версий

## Структура файлов

```
backend/
  ├── Data/
  │   ├── Entities/
  │   │   ├── DialogEntity.cs
  │   │   ├── MessageEntity.cs
  │   │   ├── MessageVersionEntity.cs
  │   │   └── DialogActivePathEntity.cs
  │   ├── Repositories/
  │   │   ├── IDialogRepository.cs
  │   │   ├── DialogRepository.cs
  │   │   ├── IMessageRepository.cs
  │   │   └── MessageRepository.cs
  │   ├── Mappers/
  │   │   └── DtoMapper.cs
  │   ├── Migrations/
  │   │   └── 20260126195625_InitialCreate.cs
  │   ├── ApplicationDbContext.cs
  │   ├── ApplicationDbContextFactory.cs
  │   └── LexoRankGenerator.cs
  ├── Controllers/
  │   └── DialogsControllerNew.cs
  ├── Services/
  │   └── ChatWorkflowServiceNew.cs
  └── Models/
      └── Dtos.cs (обновлён)

frontend/
  ├── .env.development (новый)
  └── src/
      └── App.tsx (обновлён)
```

## Следующие шаги

- [ ] Удалить старый код (InMemoryChatStore, старый ChatWorkflowService, старый контроллер)
- [ ] Добавить UI для визуализации дерева веток
- [ ] Добавить возможность именования веток
- [ ] Добавить удаление старых веток
- [ ] Оптимизировать загрузку сообщений (eager loading)
- [ ] Добавить кэширование для часто используемых запросов
