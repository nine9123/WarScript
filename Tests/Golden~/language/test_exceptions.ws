# ──────────────────────────────────────────────────────
# test_exceptions.ws
# Covers: begin/rescue/ensure, raise, rescue variable,
#         ensure always runs, class-based exceptions,
#         nested begin/rescue, exception in functions,
#         exception stops execution
# ──────────────────────────────────────────────────────

# ── Basic raise and rescue ──
caught = false
caught_val = null
begin
    raise "boom"
rescue e
    caught = true
    caught_val = e
end
assert caught
assert caught_val == "boom"

# ── Rescue prevents crash ──
result = "before"
begin
    result = "inside"
    raise "error"
    result = "unreachable"
rescue e
    result = "rescued"
end
assert result == "rescued"

# ── Ensure always runs (with exception) ──
ensure_ran = false
caught_msg = null
begin
    raise "error"
rescue e
    caught_msg = e
ensure
    ensure_ran = true
end
assert ensure_ran
assert caught_msg == "error"

# ── Ensure always runs (without exception) ──
ensure_ran2 = false
begin
    x = 42
ensure
    ensure_ran2 = true
end
assert ensure_ran2

# ── Ensure runs even when rescue handles it ──
log = {}
begin
    log << "begin"
    raise "fail"
    log << "unreachable"
rescue e
    log << "rescue"
ensure
    log << "ensure"
end
assert log == {"begin", "rescue", "ensure"}

# ── Rescue without ensure ──
rescued_val = null
begin
    raise "test error"
rescue e
    rescued_val = e
end
assert rescued_val == "test error"

# ── Class-based exception ──
class MyError [message]
end

rescued_msg = null
begin
    raise new MyError ["something went wrong"]
rescue err
    rescued_msg = err :: message
end
assert rescued_msg == "something went wrong"

# ── Multiple class exceptions ──
class NotFoundError [entity]
end
class ValidationError [field]
end

# Raise NotFoundError
error_type = null
begin
    raise new NotFoundError ["user"]
rescue err
    if err is NotFoundError
        error_type = "not_found"
    end
end
assert error_type == "not_found"

# Raise ValidationError
error_type2 = null
begin
    raise new ValidationError ["email"]
rescue err
    if err is ValidationError
        error_type2 = "validation"
    end
end
assert error_type2 == "validation"

# ── Exception in function ──
fun risky_function []
    raise "function error"
end

caught_func = false
caught_func_val = null
begin
    risky_function []
rescue e
    caught_func = true
    caught_func_val = e
end
assert caught_func
assert caught_func_val == "function error"

# ── Exception stops execution flow ──
steps = {}
fun step1 []
    steps << "step1"
end
fun step2 []
    steps << "step2"
    raise "fail at step2"
    steps << "unreachable"
end
fun step3 []
    steps << "step3"
end

begin
    step1 []
    step2 []
    step3 []
rescue e
    steps << "rescued"
end
assert steps == {"step1", "step2", "rescued"}

# ── Nested begin/rescue ──
outer_caught = false
inner_caught = false
begin
    begin
        raise "inner error"
    rescue e
        inner_caught = true
    end
rescue e
    outer_caught = true
end
assert inner_caught
assert !outer_caught

# ── Exception with class hierarchy ──
class BaseException [msg]
end
class SpecificException [msg, code] : BaseException [msg]
end

result = null
begin
    raise new SpecificException ["detailed error", 404]
rescue e
    if e is SpecificException
        result = e :: code
    end
end
assert result == 404

# ── Multiple ensures track state ──
state = "start"
begin
    state = "in begin"
    raise "fail"
rescue e
    state = "in rescue"
ensure
    state = state + " -> ensure"
end
assert state == "in rescue -> ensure"

# ── Exception in loop ──
results = {}
loop i in 0..5
    caught_in_loop = false
    begin
        if i == 3
            raise "skip"
        end
        results << i
    rescue e
        results << "skipped"
    end
end
assert results == {0, 1, 2, "skipped", 4}
