# Exception patterns: custom exception hierarchies, retry,
# exception data, nested rescue, cleanup patterns

# ── 1. Custom exception hierarchy ──
class AppError [message, code]
end
class HttpError [message, code, status] : AppError [message, code]
end
class NotFound [message, code, path] : HttpError [message, code, code]
end
class ServerError [message, code] : HttpError [message, code, code]
end

# ── 2. Exception handler dispatch ──
fun handle [err]
    if err is NotFound
        nf = err as NotFound
        return "404: " + nf :: path
    elif err is ServerError
        return "500: " + err :: message
    elif err is HttpError
        return "HTTP " + err :: code
    elif err is AppError
        return "App: " + err :: message
    end
    return "unknown"
end

result = null
begin
    raise new NotFound ["not found", 404, "/api/user"]
rescue e
    result = handle [e]
end
assert result == "404: /api/user"

result = null
begin
    raise new ServerError ["disk full", 500]
rescue e
    result = handle [e]
end
assert result == "500: disk full"

# ── 3. Instanceof checks on exception ──
err = new NotFound ["missing", 404, "/test"]
assert err is NotFound
assert err is HttpError
assert err is AppError

# ── 4. Retry pattern ──
attempt = 0
success = false
loop attempt < 3 and !success
    begin
        attempt += 1
        if attempt < 3
            raise "fail"
        end
        success = true
    rescue e
        # retry
    end
end
assert attempt == 3
assert success

# ── 5. Collecting errors from loop ──
errors = {}
loop i in 0..5
    begin
        if i == 1
            raise new AppError ["bad input", 400]
        elif i == 3
            raise new AppError ["conflict", 409]
        end
    rescue e
        errors << e :: code
    end
end
assert errors == {400, 409}

# ── 6. Exception with array data ──
class BatchError [failed_items]
end

failed = {}
loop i in 0..8
    if i % 3 == 0
        failed << i
    end
end

caught_items = null
begin
    raise new BatchError [failed]
rescue e
    caught_items = e :: failed_items
end
assert caught_items == {0, 3, 6}

# ── 7. Ensure runs before rescue propagation ──
cleanup_order = {}
begin
    begin
        cleanup_order << "inner_begin"
        raise "inner"
    ensure
        cleanup_order << "inner_ensure"
    end
rescue e
    cleanup_order << "outer_rescue"
ensure
    cleanup_order << "outer_ensure"
end
assert cleanup_order == {"inner_begin", "inner_ensure", "outer_rescue", "outer_ensure"}

# ── 8. Function that always cleans up ──
resource_open = false
fun use_resource []
    resource_open = true
    begin
        raise "oops"
    ensure
        resource_open = false
    end
end

begin
    use_resource []
rescue e
    # caught
end
assert !resource_open

# ── 9. Exception message interpolation ──
user_id = 42
err_msg = null
begin
    raise "User {user_id} not found"
rescue e
    err_msg = e
end
assert err_msg == "User 42 not found"

# ── 10. Sequential error handling ──
log = {}
loop i in 0..4
    ok = false
    begin
        if i % 2 == 0
            raise "even_error"
        end
        ok = true
    rescue e
        log << "caught at " + i
    end
    if ok
        log << "ok at " + i
    end
end
assert log == {"caught at 0", "ok at 1", "caught at 2", "ok at 3"}
