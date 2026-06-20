# ──────────────────────────────────────────────────────
# test_regression_is_null.ws
# Bug: is_null checks for CLR null (args[0] == null)
#      but WarScript null is a NullValue singleton, so
#      is_null[x] always returns false for WarScript null.
# ──────────────────────────────────────────────────────

# ── Literal null should be detected ──
assert is_null[null] == true

# ── Uninitialized variable defaults to null ──
assert is_null[undefined_var] == true

# ── Non-null values should return false ──
assert is_null[0] == false
assert is_null[42] == false
assert is_null[""] == false
assert is_null["hello"] == false
assert is_null[true] == false
assert is_null[false] == false

# ── Variable set to null ──
x = null
assert is_null[x] == true

# ── Variable set then cleared ──
y = 10
y = null
assert is_null[y] == true

# ── Array element that is null ──
arr = {null, 1, 2}
assert is_null[arr{0}] == true
assert is_null[arr{1}] == false
