# ──────────────────────────────────────────────────────
# test_conditionals.ws
# Covers: if/elif/else/end, nested conditionals,
#         conditions with all operator types
# ──────────────────────────────────────────────────────

# ── Simple if true ──
result = "none"
if true
    result = "yes"
end
assert result == "yes"

# ── Simple if false (no else) ──
result = "none"
if false
    result = "yes"
end
assert result == "none"

# ── If / else (pre-declare variable) ──
r1 = "unset"
if true
    r1 = "a"
else
    r1 = "b"
end
assert r1 == "a"

r2 = "unset"
if false
    r2 = "a"
else
    r2 = "b"
end
assert r2 == "b"

# ── If / elif / else ──
x = 5
label = "unset"
if x > 10
    label = "big"
elif x > 3
    label = "medium"
else
    label = "small"
end
assert label == "medium"

# ── First elif matches ──
x = 15
label = "unset"
if x > 100
    label = "huge"
elif x > 10
    label = "big"
elif x > 5
    label = "medium"
else
    label = "small"
end
assert label == "big"

# ── Else when nothing matches ──
x = 1
label = "unset"
if x > 100
    label = "huge"
elif x > 50
    label = "big"
elif x > 10
    label = "medium"
else
    label = "small"
end
assert label == "small"

# ── Multiple elif chains ──
grade = 75
letter = "unset"
if grade >= 90
    letter = "A"
elif grade >= 80
    letter = "B"
elif grade >= 70
    letter = "C"
elif grade >= 60
    letter = "D"
else
    letter = "F"
end
assert letter == "C"

# ── Nested if blocks ──
a = 10
b = 20
nested_result = "unset"
if a > 5
    if b > 15
        nested_result = "both"
    else
        nested_result = "only a"
    end
else
    nested_result = "neither"
end
assert nested_result == "both"

# ── Deeply nested ──
level = 3
deep = "unset"
if level > 0
    if level > 1
        if level > 2
            deep = "very deep"
        else
            deep = "deep"
        end
    else
        deep = "shallow"
    end
else
    deep = "surface"
end
assert deep == "very deep"

# ── Conditions with and ──
age = 25
has_license = true
can_drive = false
if age >= 18 and has_license
    can_drive = true
end
assert can_drive

# ── Conditions with or ──
is_admin = false
is_moderator = true
has_access = false
if is_admin or is_moderator
    has_access = true
end
assert has_access

# ── Conditions with not ──
is_blocked = false
allowed = false
if !is_blocked
    allowed = true
end
assert allowed

# ── Conditions with complex expressions ──
score = 85
bonus = 10
rank = "unset"
if (score + bonus) >= 90
    rank = "gold"
else
    rank = "silver"
end
assert rank == "gold"

# ── Conditions with function calls ──
fun is_even [n]
    return n % 2 == 0
end

even_test = "no"
if is_even [4]
    even_test = "yes"
end
assert even_test == "yes"

even_test2 = "no"
if is_even [7]
    even_test2 = "yes"
end
assert even_test2 == "no"

# ── Condition with string comparison ──
name = "Alice"
greeting = "unset"
if name == "Alice"
    greeting = "Hi Alice!"
elif name == "Bob"
    greeting = "Hi Bob!"
else
    greeting = "Hello stranger"
end
assert greeting == "Hi Alice!"

# ── Condition with null check ──
maybe_val = null
null_check = "unset"
if maybe_val == null
    null_check = "is null"
else
    null_check = "has value"
end
assert null_check == "is null"

maybe_val = 42
null_check2 = "unset"
if maybe_val != null
    null_check2 = "has value"
else
    null_check2 = "is null"
end
assert null_check2 == "has value"

# ── If inside loop ──
evens = {}
loop i in 0..10
    if i % 2 == 0
        evens << i
    end
end
assert evens == {0, 2, 4, 6, 8}

# ── Condition modifies shared variable ──
state = "init"
if true
    state = "modified"
end
assert state == "modified"
if false
    state = "should not happen"
end
assert state == "modified"
