using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Data.Sqlite;

namespace TaskbarCalendar.Data;

/// <summary>
/// 时光序事项的本地缓存表。面板只读这里，同步服务负责把远端数据灌进来。
/// 与 todo_item 完全分表，管理窗口和 WebDAV 同步不受影响。
/// </summary>
public sealed class SgxDatabase
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string TimeFormat = "yyyy-MM-ddTHH:mm:ss.fff";

    private const string TagOrder = "CASE tag WHEN 1 THEN 0 WHEN 3 THEN 1 WHEN 2 THEN 2 ELSE 3 END";

    private readonly string DatabasePath;

    public SgxDatabase(string _database_path)
    {
        DatabasePath = _database_path;
    }

    public void Initialize()
    {
        string? _directory = Path.GetDirectoryName(DatabasePath);
        if (string.IsNullOrEmpty(_directory) is false)
        {
            Directory.CreateDirectory(_directory);
        }

        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = """
            CREATE TABLE IF NOT EXISTS sgx_item (
                id           TEXT    PRIMARY KEY,
                title        TEXT    NOT NULL,
                note         TEXT    NOT NULL DEFAULT '',
                due_date     TEXT,
                kind         INTEGER NOT NULL DEFAULT 1,
                tag          INTEGER NOT NULL DEFAULT 0,
                completed_at TEXT,
                deleted_at   TEXT,
                todo_type    INTEGER NOT NULL DEFAULT 1,
                todo_time    TEXT,
                repeat_type  INTEGER NOT NULL DEFAULT 0,
                start_time   TEXT,
                end_time     TEXT,
                updated_at   TEXT    NOT NULL
            );

            CREATE INDEX IF NOT EXISTS idx_sgx_due ON sgx_item(due_date, kind);
            """;
        _command.ExecuteNonQuery();

        // - 旧缓存表补上回写需要的列
        EnsureColumn(_connection, "todo_type", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(_connection, "todo_time", "TEXT");
        EnsureColumn(_connection, "repeat_type", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(_connection, "start_time", "TEXT");
        EnsureColumn(_connection, "end_time", "TEXT");
    }

    private static void EnsureColumn(SqliteConnection _connection, string _name, string _definition)
    {
        using SqliteCommand _check = _connection.CreateCommand();
        _check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('sgx_item') WHERE name = $name";
        _check.Parameters.AddWithValue("$name", _name);
        if (Convert.ToInt64(_check.ExecuteScalar() ?? 0L) > 0)
        {
            return;
        }

        using SqliteCommand _alter = _connection.CreateCommand();
        _alter.CommandText = $"ALTER TABLE sgx_item ADD COLUMN {_name} {_definition}";
        _alter.ExecuteNonQuery();
    }

    /// <summary>
    /// 用一次完整拉取的结果覆盖本地缓存：存在的按 id 更新，远端没有的标记为删除。
    /// </summary>
    public void ApplyItems(IReadOnlyList<SyncItem> _items, string _synced_at)
    {
        using SqliteConnection _connection = Open();
        using SqliteTransaction _transaction = _connection.BeginTransaction();

        HashSet<string> _seen = [];
        foreach (SyncItem _item in _items)
        {
            _seen.Add(_item.Id);

            using SqliteCommand _command = _connection.CreateCommand();
            _command.Transaction = _transaction;
            _command.CommandText = """
                INSERT INTO sgx_item (id, title, note, due_date, kind, tag, completed_at, deleted_at, todo_type, todo_time, repeat_type, start_time, end_time, updated_at)
                VALUES ($id, $title, $note, $due_date, $kind, $tag, $completed_at, $deleted_at, $todo_type, $todo_time, $repeat_type, $start_time, $end_time, $updated_at)
                ON CONFLICT(id) DO UPDATE SET
                    title = excluded.title,
                    note = excluded.note,
                    due_date = excluded.due_date,
                    kind = excluded.kind,
                    tag = excluded.tag,
                    completed_at = excluded.completed_at,
                    deleted_at = excluded.deleted_at,
                    todo_type = excluded.todo_type,
                    todo_time = excluded.todo_time,
                    repeat_type = excluded.repeat_type,
                    start_time = excluded.start_time,
                    end_time = excluded.end_time,
                    updated_at = excluded.updated_at
                """;
            _command.Parameters.AddWithValue("$id", _item.Id);
            _command.Parameters.AddWithValue("$title", _item.Title);
            _command.Parameters.AddWithValue("$note", _item.Note);
            _command.Parameters.AddWithValue("$due_date", (object?)_item.DueDate ?? DBNull.Value);
            _command.Parameters.AddWithValue("$kind", _item.Kind);
            _command.Parameters.AddWithValue("$tag", _item.Tag);
            _command.Parameters.AddWithValue("$completed_at", (object?)_item.CompletedAt ?? DBNull.Value);
            _command.Parameters.AddWithValue("$deleted_at", (object?)_item.DeletedAt ?? DBNull.Value);
            _command.Parameters.AddWithValue("$todo_type", _item.TodoType);
            _command.Parameters.AddWithValue("$todo_time", (object?)_item.TodoTime ?? DBNull.Value);
            _command.Parameters.AddWithValue("$repeat_type", _item.RepeatType);
            _command.Parameters.AddWithValue("$start_time", (object?)_item.StartTime ?? DBNull.Value);
            _command.Parameters.AddWithValue("$end_time", (object?)_item.EndTime ?? DBNull.Value);
            _command.Parameters.AddWithValue("$updated_at", _item.UpdatedAt);
            _command.ExecuteNonQuery();
        }

        // - 本次没拉到的记录：本地缓存里补上删除标记，但仍保留行，方便排查
        List<string> _missing = [];
        using (SqliteCommand _list = _connection.CreateCommand())
        {
            _list.Transaction = _transaction;
            _list.CommandText = "SELECT id FROM sgx_item WHERE deleted_at IS NULL";
            using SqliteDataReader _reader = _list.ExecuteReader();
            while (_reader.Read())
            {
                string _id = _reader.GetString(0);
                if (_seen.Contains(_id) is false)
                {
                    _missing.Add(_id);
                }
            }
        }

        foreach (string _id in _missing)
        {
            using SqliteCommand _tombstone = _connection.CreateCommand();
            _tombstone.Transaction = _transaction;
            _tombstone.CommandText = "UPDATE sgx_item SET deleted_at = $now WHERE id = $id";
            _tombstone.Parameters.AddWithValue("$now", _synced_at);
            _tombstone.Parameters.AddWithValue("$id", _id);
            _tombstone.ExecuteNonQuery();
        }

        _transaction.Commit();
    }

    #region 查询

    public IReadOnlyList<TodoItemRecord> GetByDate(DateOnly _date)
    {
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = $"""
            SELECT id, title, kind, tag, note, todo_type
            FROM sgx_item
            WHERE due_date = $date AND deleted_at IS NULL
            ORDER BY CASE kind WHEN 1 THEN 0 WHEN 0 THEN 1 ELSE 2 END, {TagOrder}, title
            """;
        _command.Parameters.AddWithValue("$date", _date.ToString(DateFormat, CultureInfo.InvariantCulture));
        return ReadRecords(_command);
    }

    public IReadOnlyList<TodoItemRecord> GetWithoutDate()
    {
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        // - 今天刚勾完的无日期事项保留在列表里，允许点回去撤销
        _command.CommandText = $"""
            SELECT id, title, kind, tag, note, todo_type
            FROM sgx_item
            WHERE due_date IS NULL AND deleted_at IS NULL
              AND (kind IN (0, 1) OR (kind = 2 AND substr(completed_at, 1, 10) = $today))
            ORDER BY CASE kind WHEN 1 THEN 0 WHEN 0 THEN 1 ELSE 2 END, {TagOrder}, title
            """;
        _command.Parameters.AddWithValue("$today", DateOnly.FromDateTime(DateTime.Today).ToString(DateFormat, CultureInfo.InvariantCulture));
        return ReadRecords(_command);
    }

    public HashSet<DateOnly> GetDatesWithSingleItems(DateOnly _start, DateOnly _end)
    {
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = """
            SELECT DISTINCT due_date
            FROM sgx_item
            WHERE kind IN (0, 1) AND deleted_at IS NULL AND due_date IS NOT NULL AND due_date BETWEEN $start AND $end
            """;
        _command.Parameters.AddWithValue("$start", _start.ToString(DateFormat, CultureInfo.InvariantCulture));
        _command.Parameters.AddWithValue("$end", _end.ToString(DateFormat, CultureInfo.InvariantCulture));

        HashSet<DateOnly> _dates = [];
        using SqliteDataReader _reader = _command.ExecuteReader();
        while (_reader.Read())
        {
            if (DateOnly.TryParseExact(_reader.GetString(0), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly _date))
            {
                _dates.Add(_date);
            }
        }

        return _dates;
    }

    /// <summary>
    /// 某月里每个日期出现过的事项标签，用于日历下方的彩色圆点。
    /// </summary>
    public List<(DateOnly Date, int Tag)> GetMonthTags(DateOnly _start, DateOnly _end)
    {
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = """
            SELECT due_date, tag
            FROM sgx_item
            WHERE deleted_at IS NULL AND due_date IS NOT NULL AND due_date BETWEEN $start AND $end
            """;
        _command.Parameters.AddWithValue("$start", _start.ToString(DateFormat, CultureInfo.InvariantCulture));
        _command.Parameters.AddWithValue("$end", _end.ToString(DateFormat, CultureInfo.InvariantCulture));

        List<(DateOnly, int)> _rows = [];
        using SqliteDataReader _reader = _command.ExecuteReader();
        while (_reader.Read())
        {
            if (DateOnly.TryParseExact(_reader.GetString(0), DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly _date))
            {
                _rows.Add((_date, _reader.GetInt32(1)));
            }
        }

        return _rows;
    }

    /// <summary>
    /// 某一天的事项（面板「今天」用）。
    /// </summary>
    public IReadOnlyList<SyncItem> QueryItems(string _scope, string? _search)
    {
        string _where = _scope switch
        {
            "today" => "deleted_at IS NULL AND ((kind IN (0, 1, 2) AND due_date = $today)"
                + " OR (kind = 1 AND due_date IS NOT NULL AND due_date < $today)"
                + " OR (kind = 2 AND substr(completed_at, 1, 10) = $today AND (due_date IS NULL OR due_date <= $today)))",
            _ => "deleted_at IS NULL AND kind IN (0, 1)"
        };

        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = $"""
            SELECT id, title, note, due_date, kind, tag, completed_at, deleted_at, todo_type, todo_time, repeat_type, start_time, end_time, updated_at
            FROM sgx_item
            WHERE {_where}
            """;
        _command.Parameters.AddWithValue("$today", DateOnly.FromDateTime(DateTime.Today).ToString(DateFormat, CultureInfo.InvariantCulture));

        if (string.IsNullOrWhiteSpace(_search) is false)
        {
            _command.CommandText += " AND (title LIKE $search OR note LIKE $search)";
            _command.Parameters.AddWithValue("$search", $"%{_search.Trim()}%");
        }

        _command.CommandText += $" ORDER BY due_date IS NULL, due_date, {TagOrder}, title";

        List<SyncItem> _items = [];
        using SqliteDataReader _reader = _command.ExecuteReader();
        while (_reader.Read())
        {
            _items.Add(ReadSyncItem(_reader));
        }

        return _items;
    }

    /// <summary>
    /// 按 id 取一条，供回写时读取类型与原始时间。
    /// </summary>
    public SyncItem? GetItem(string _id)
    {
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = $"""
            SELECT id, title, note, due_date, kind, tag, completed_at, deleted_at, todo_type, todo_time, repeat_type, start_time, end_time, updated_at
            FROM sgx_item WHERE id = $id
            """;
        _command.Parameters.AddWithValue("$id", _id);

        using SqliteDataReader _reader = _command.ExecuteReader();
        return _reader.Read() ? ReadSyncItem(_reader) : null;
    }

    /// <summary>
    /// 只改本地状态，用于勾选后先给界面反馈；回写失败时调用方负责回滚。
    /// </summary>
    public void SetKind(string _id, int _kind)
    {
        string _now = DateTime.Now.ToString(TimeFormat);
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = _kind == TodoKind.Done
            ? "UPDATE sgx_item SET kind = $kind, completed_at = $now WHERE id = $id"
            : "UPDATE sgx_item SET kind = $kind, completed_at = NULL WHERE id = $id";
        _command.Parameters.AddWithValue("$kind", _kind);
        _command.Parameters.AddWithValue("$now", _now);
        _command.Parameters.AddWithValue("$id", _id);
        _command.ExecuteNonQuery();
    }

    /// <summary>
    /// 带重复规则的事项，供面板按规则展开到各日期。
    /// </summary>
    public IReadOnlyList<SyncItem> GetRepeatItems()
    {
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = """
            SELECT id, title, note, due_date, kind, tag, completed_at, deleted_at, todo_type, todo_time, repeat_type, start_time, end_time, updated_at
            FROM sgx_item
            WHERE deleted_at IS NULL AND repeat_type > 0 AND todo_time IS NOT NULL
            """;

        List<SyncItem> _items = [];
        using SqliteDataReader _reader = _command.ExecuteReader();
        while (_reader.Read())
        {
            _items.Add(ReadSyncItem(_reader));
        }

        return _items;
    }

    public int Count()
    {
        using SqliteConnection _connection = Open();
        using SqliteCommand _command = _connection.CreateCommand();
        _command.CommandText = "SELECT COUNT(*) FROM sgx_item WHERE deleted_at IS NULL";
        return Convert.ToInt32(_command.ExecuteScalar() ?? 0);
    }

    #endregion

    #region 内部

    private SqliteConnection Open()
    {
        SqliteConnectionStringBuilder _builder = new()
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        };

        SqliteConnection _connection = new(_builder.ToString());
        _connection.Open();
        return _connection;
    }

    private static SyncItem ReadSyncItem(SqliteDataReader _reader)
    {
        return new SyncItem
        {
            Id = _reader.GetString(0),
            Title = _reader.GetString(1),
            Note = _reader.GetString(2),
            DueDate = _reader.IsDBNull(3) ? null : _reader.GetString(3),
            Kind = _reader.GetInt32(4),
            Tag = _reader.GetInt32(5),
            CompletedAt = _reader.IsDBNull(6) ? null : _reader.GetString(6),
            DeletedAt = _reader.IsDBNull(7) ? null : _reader.GetString(7),
            TodoType = _reader.GetInt32(8),
            TodoTime = _reader.IsDBNull(9) ? null : _reader.GetString(9),
            RepeatType = _reader.GetInt32(10),
            StartTime = _reader.IsDBNull(11) ? null : _reader.GetString(11),
            EndTime = _reader.IsDBNull(12) ? null : _reader.GetString(12),
            UpdatedAt = _reader.GetString(13)
        };
    }

    private static IReadOnlyList<TodoItemRecord> ReadRecords(SqliteCommand _command)
    {
        List<TodoItemRecord> _records = [];
        using SqliteDataReader _reader = _command.ExecuteReader();
        while (_reader.Read())
        {
            _records.Add(new TodoItemRecord(
                _reader.GetString(0),
                _reader.GetString(1),
                _reader.GetInt32(2),
                _reader.GetInt32(3),
                _reader.IsDBNull(4) ? "" : _reader.GetString(4),
                _reader.GetInt32(5)));
        }

        return _records;
    }

    #endregion
}
