# ──────────────────────────────────────────────────────
# test_string_operations.ws
# Covers: concatenation, repetition, subtraction,
#         character indexing, comparisons, empty strings,
#         string + number coercion
# ──────────────────────────────────────────────────────

# ── String concatenation ──
assert "hello" + " " + "world" == "hello world"
assert "" + "a" == "a"
assert "a" + "" == "a"
assert "" + "" == ""

# ── Concatenation with numbers ──
assert "val: " + 42 == "val: 42"
assert 42 + " is the answer" == "42 is the answer"
assert "pi: " + 3.14 == "pi: 3.14"

# ── Concatenation with booleans ──
assert "flag: " + true == "flag: True"
assert "flag: " + false == "flag: False"

# ── String repetition ──
assert "abc" * 0 == ""
assert "abc" * 1 == "abc"
assert "abc" * 2 == "abcabc"
assert "abc" * 3 == "abcabcabc"
assert "-" * 5 == "-----"

# ── Repetition commutative ──
assert 3 * "xy" == "xyxyxy"

# ── String subtraction (Replace removes ALL occurrences) ──
assert "hello world" - "world" == "hello "
assert "hello world" - "hello " == "world"
assert "abcabc" - "abc" == ""
assert "test" - "xyz" == "test"
assert "aaa" - "a" == ""
assert "banana" - "na" == "ba"

# ── Character indexing (must use variable, not literal) ──
s = "hello"
assert s{0} == "h"
assert s{1} == "e"
assert s{2} == "l"
assert s{3} == "l"
assert s{4} == "o"

# ── Indexing single char strings (Bug 5 fix: literal indexing) ──
single_char = "x"
assert single_char{0} == "x"
assert "hello"{0} == "h"
assert "hello"{4} == "o"
assert "abc"{1} == "b"

# ── String equality ──
assert "abc" == "abc"
assert "abc" != "def"
assert "" == ""
assert " " != ""
assert "ABC" != "abc"

# ── String comparison (lexicographic) ──
assert "a" < "b"
assert "b" > "a"
assert "apple" < "banana"
assert "z" > "a"
assert "abc" < "abd"
assert "abc" <= "abc"
assert "abc" >= "abc"

# ── Multipart concatenation ──
first = "John"
last = "Doe"
full = first + " " + last
assert full == "John Doe"

# ── Building strings in loop ──
s = ""
loop i in 0..5
    s += i
end
assert s == "01234"

# ── String concatenation precedence ──
assert "result: " + 2 + 3 == "result: 23"
assert "result: " + (2 + 3) == "result: 5"

# ── Empty string behavior ──
empty = ""
assert empty == ""
assert empty + "x" == "x"
assert "x" + empty == "x"
assert empty * 100 == ""

# ── String in array ──
arr = {"hello", "world"}
assert arr{0} == "hello"
assert arr{1} == "world"
assert arr{0} + " " + arr{1} == "hello world"

# ── String from function ──
fun greet [name]
    return "Hello, " + name + "!"
end
assert greet ["Alice"] == "Hello, Alice!"
assert greet [""] == "Hello, !"
