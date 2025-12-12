# КОНСТРУИРОВАНИЕ ПРОГРАММНОГО ОБЕСПЕЧЕНИЯ
# Домашнее задание №3 — Микросервисная система проверки работ на плагиат

## 1. Краткое описание проекта

Проект - микросервисная система, которая позволяет студентам отправлять работы, а преподавателям получать отчёты о плагиате и вспомогательные артефакты (например, облако слов). Система реализована на C# (.NET 8) и состоит из трёх сервисов:

* API Gateway - единая точка входа для клиентов, координирует загрузку и анализ.
* FileService - хранение загруженных файлов и артефактов (PNG) с метаданными.
* AnalysisService - регистрация сдач, идемпотентность, вычисление хэша файла, проверка плагиата, формирование отчётов.

Данные хранятся в PostgreSQL, сервисы разворачиваются через Docker Compose.

# 2. Сборка и запуск

## 2.1 Требования

* Docker (версия 20+)
* Docker Compose
* Свободные порты:

    * 8080 — API Gateway + Swagger
    * 8081 — FileService + Swagger
    * 8082 — AnalysisService + Swagger
    * 5432 — PostgreSQL (опционально, для просмотра БД)

## 2.2 Инструкция по запуску

1. Клонировать репозиторий и перейти в папку проекта:
   git clone https://github.com/pm4o/PlagiarismApp-HW3.git

2. Поднять всю систему:
   docker compose up --build

## 2.3 Доступные сервисы после запуска

* API Gateway (единая точка входа), Swagger UI:
  [http://localhost:8080/swagger](http://localhost:8080/swagger)

* FileService (хранение файлов), Swagger UI:
  [http://localhost:8081/swagger](http://localhost:8081/swagger)

* AnalysisService (проверка и отчёты), Swagger UI:
  [http://localhost:8082/swagger](http://localhost:8082/swagger)

# 3. Общая идея решения

Система реализует полный сценарий сдачи и анализа работы:

1. Клиент (студент) отправляет файл и метаданные (studentId, assignmentId) через Gateway.
2. Gateway обеспечивает идемпотентность операции по заголовку Idempotency-Key.
3. Файл сохраняется в FileService и получает fileId.
4. AnalysisService скачивает файл по fileId, считает SHA-256, фиксирует работу в БД и выполняет проверку плагиата.
5. На выходе возвращается SubmissionResult с флагом плагиата и списком отчётов:

    * Plagiarism — обязательный отчёт.
    * WordCloud — дополнительный отчёт с артефактом artifactFileId (PNG), если доступна генерация.

# 4. Архитектура (состав сервисов)

## 4.1 API Gateway

Роль:

* единая точка входа для клиентов;
* выполняет оркестрацию: Start → Upload → AttachFile → Analyze;
* проксирует чтение работ и отчётов;
* проксирует скачивание файлов и артефактов.

Ключевые эндпоинты:

* POST /works — студент сдаёт работу (multipart/form-data)
* GET /works/{workId} — получить работу
* GET /works/{workId}/reports — получить отчёты
* GET /files/{fileId} — скачать файл/артефакт
* GET /health

## 4.2 FileService

Роль:

* хранит бинарные файлы и их метаданные;
* отдаёт файл по fileId;
* поддерживает два вида загрузки: multipart и raw.

Эндпоинты:

* POST /files — multipart upload
* POST /files/raw — raw upload (для артефактов)
* GET /files/{fileId} — скачать
* GET /files/{fileId}/meta — метаданные
* GET /health

## 4.3 AnalysisService

Роль:

* доменная логика сдач: создание, статусы, отчёты;
* вычисляет SHA-256 файла;
* проверка плагиата по хэшу;
* хранение в Postgres: Works, Reports, Submissions (идемпотентность).

Эндпоинты:

* POST /internal/submissions/start
* POST /internal/works/{workId}/attach-file
* POST /internal/works/{workId}/upload-failed
* POST /internal/works/{workId}/analyze
* GET /works/{workId}
* GET /works/{workId}/reports
* GET /health

# 5. Идея проверки плагиата

Для определения плагиата используется SHA-256 хэш содержимого файла, а не имя файла.

Алгоритм для работы W:

1. Скачивается файл по fileId из FileService.
2. Вычисляется fileHashSha256.
3. По заданию assignmentId ищется более ранняя работа другого студента с тем же хэшем:

    * если найдена - plagiarism = true, plagiarismSourceWorkId = найденный WorkId;
    * иначе - plagiarism = false.

Таким образом, плагиат определяется как полное совпадение содержимого файла, что соответствует простому и прозрачному критерию.

# 6. Идемпотентность

Операция POST /works должна быть безопасной при повторной отправке (например, из-за сетевых ошибок или повторной кнопки).

Механизм:

* Клиент передаёт заголовок Idempotency-Key.
* Gateway и AnalysisService вычисляют requestHash по входным данным.
* Если запрос повторяется с тем же ключом и тем же requestHash - возвращается тот же результат, без создания новых работ и отчётов.
* Если повторяется с тем же ключом, но другим содержимым - возвращается 409 Conflict:
  "Idempotency-Key conflict (same key used with different payload)"

# 7. Пользовательские сценарии и технический обмен между сервисами

## 7.1 Сценарий 1: студент отправляет работу

Действия пользователя: студент загружает файл сдачи.

Вызов:
POST /works (Gateway), multipart/form-data:

* studentId
* studentName
* assignmentId
* file

Технический обмен:

1. Gateway проверяет Idempotency-Key.
2. Gateway вызывает AnalysisService: POST /internal/submissions/start.
3. Gateway загружает файл в FileService: POST /files → получает fileId.
4. Gateway прикрепляет fileId к работе: POST /internal/works/{workId}/attach-file.
5. Gateway запускает анализ: POST /internal/works/{workId}/analyze.
6. Gateway возвращает клиенту SubmissionResult.

## 7.2 Сценарий 2: преподаватель получает информацию о конкретной работе

Вызов:
GET /works/{workId}

Что происходит:

* Gateway проксирует запрос к AnalysisService.
* Возвращаются данные по работе: кто сдал, какое задание, статус, fileId, hash, plagiarism.

## 7.3 Сценарий 3: преподаватель получает отчёты по работе

Вызов:
GET /works/{workId}/reports

Что происходит:

* Gateway проксирует запрос к AnalysisService.
* Возвращается список отчётов:

    * Plagiarism (JSON результат)
    * WordCloud (JSON результат + artifactFileId, если сформировано)

## 7.4 Сценарий 4: скачивание артефактов и файлов

Вызов:
GET /files/{fileId}

Что происходит:

* Gateway проксирует запрос в FileService.
* Клиент получает бинарный поток (например, wordcloud.png).

# 8. Обработка ошибок и отказоустойчивость

* Если FileService недоступен при загрузке, Gateway возвращает корректную ошибку (502/503), а AnalysisService помечает работу статусом FileUploadFailed через endpoint upload-failed.
* Генерация WordCloud выполняется по принципу best-effort: если QuickChart недоступен, отчёт WordCloud помечается как Failed, но основной анализ (плагиат) остаётся успешным.

# 9. Контейнеризация и инфраструктура

* Каждый сервис имеет свой Dockerfile.
* docker-compose.yml поднимает: postgres, files, analysis, gateway.
* Внутренняя сеть Docker обеспечивает общение сервисов по именам (files, analysis, postgres).
* Файлы и данные БД сохраняются в Docker volumes.

# 10. Swagger и проверка работоспособности

Swagger UI:

* Gateway: [http://localhost:8080/swagger](http://localhost:8080/swagger)
* FileService: [http://localhost:8081/swagger](http://localhost:8081/swagger)
* AnalysisService: [http://localhost:8082/swagger](http://localhost:8082/swagger)

Health-check:

* GET [http://localhost:8080/health](http://localhost:8080/health)
* GET [http://localhost:8081/health](http://localhost:8081/health)
* GET [http://localhost:8082/health](http://localhost:8082/health)

# 11. Word cloud

Доступен по вызову:
GET /files/{artifactFileId}
