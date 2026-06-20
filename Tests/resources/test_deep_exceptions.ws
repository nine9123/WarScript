# ──────────────────────────────────────────────────────
# test_deep_exceptions.ws
# Deep exception tests: propagation through call chains,
# rescue with instanceof hierarchy, exception in loops,
# ensure ordering, re-raise patterns, nested try blocks
# ──────────────────────────────────────────────────────

# ── 1. Exception propagates through 5-level call chain ──
fun chain_5 []
    raise "deep"
end
fun chain_4 []
    return chain_5 []
end
fun chain_3 []
    return chain_4 []
end
fun chain_2 []
    return chain_3 []
end
fun chain_1 []
    return chain_2 []
end

caught = false
msg = null
begin
    chain_1 []
rescue e
    caught = true
    msg = e
end
assert caught
assert msg == "deep"

# ── 2. Rescue by class hierarchy ──
class AppError [message]
end
class NotFoundError [message, entity] : AppError [message]
end
class PermissionError [message, action] : AppError [message]
end

fun handle_error [err]
    if err is PermissionError
        return "permission: " + err :: action
    elif err is NotFoundError
        return "not_found: " + err :: entity
    elif err is AppError
        return "app_error: " + err :: message
    end
    return "unknown"
end

result1 = null
begin
    raise new NotFoundError ["missing", "user_42"]
rescue e
    result1 = handle_error [e]
end
assert result1 == "not_found: user_42"

result2 = null
begin
    raise new PermissionError ["denied", "delete"]
rescue e
    result2 = handle_error [e]
end
assert result2 == "permission: delete"

# ── 3. Exception in loop — each iteration isolated ──
results = {}
loop i in 0..5
    caught_in_loop = false
    begin
        if i % 2 == 0
            raise "even"
        end
        results << "ok:" + i
    rescue e
        results << "err:" + i
    end
end
assert results == {"err:0", "ok:1", "err:2", "ok:3", "err:4"}

# ── 4. Ensure runs even on successful return ──
ensure_log = {}
fun safe_operation [succeed]
    begin
        if !succeed
            raise "fail"
        end
        ensure_log << "success"
    rescue e
        ensure_log << "rescued"
    ensure
        ensure_log << "cleanup"
    end
end

safe_operation [true]
assert ensure_log == {"success", "cleanup"}

ensure_log = {}
safe_operation [false]
assert ensure_log == {"rescued", "cleanup"}

# ── 5. Nested begin/rescue — inner handles, outer continues ──
outer_log = {}
begin
    outer_log << "outer_start"
    begin
        outer_log << "inner_start"
        raise "inner_error"
        outer_log << "inner_unreachable"
    rescue e
        outer_log << "inner_rescue"
    ensure
        outer_log << "inner_ensure"
    end
    outer_log << "outer_continue"
rescue e
    outer_log << "outer_rescue"
ensure
    outer_log << "outer_ensure"
end
assert outer_log == {"outer_start", "inner_start", "inner_rescue", "inner_ensure", "outer_continue", "outer_ensure"}

# ── 6. Nested begin/rescue — inner unhandled, outer catches ──
outer_log2 = {}
begin
    outer_log2 << "begin"
    begin
        raise "bubble_up"
    ensure
        outer_log2 << "inner_ensure"
    end
    outer_log2 << "unreachable"
rescue e
    outer_log2 << "outer_caught: " + e
ensure
    outer_log2 << "outer_ensure"
end
assert outer_log2 == {"begin", "inner_ensure", "outer_caught: bubble_up", "outer_ensure"}

# ── 7. Exception halts function, caller catches ──
fun work_step [n]
    if n == 3
        raise new AppError ["step 3 failed"]
    end
    return "done:" + n
end

work_results = {}
all_ok = true
begin
    loop i in 0..5
        work_results << work_step [i]
    end
rescue e
    all_ok = false
    assert e is AppError
    assert e :: message == "step 3 failed"
end
assert !all_ok
assert work_results == {"done:0", "done:1", "done:2"}

# ── 8. Exception stores complex data ──
class DetailedError [code, message, context]
end

err_data = null
begin
    ctx = {1, 2, 3}
    raise new DetailedError [500, "Internal error", ctx]
rescue e
    err_data = e
end
assert err_data :: code == 500
assert err_data :: message == "Internal error"
assert err_data :: context == {1, 2, 3}

# ── 9. Multiple sequential begin/rescue blocks ──
log = {}
begin
    log << "first_begin"
    raise "first"
rescue e
    log << "first_rescue"
end

begin
    log << "second_begin"
rescue e
    log << "second_rescue"
end

begin
    log << "third_begin"
    raise "third"
rescue e
    log << "third_rescue"
end
assert log == {"first_begin", "first_rescue", "second_begin", "third_begin", "third_rescue"}

# ── 10. Exception in class method, caught outside ──
class Validator []
    fun validate [input]
        if input == ""
            raise new AppError ["empty input"]
        end
        if input == null
            raise new AppError ["null input"]
        end
        return true
    end
end

v = new Validator
assert v :: validate ["hello"]

caught_empty = false
begin
    v :: validate [""]
rescue e
    caught_empty = true
    assert e :: message == "empty input"
end
assert caught_empty
