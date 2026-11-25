# ──────────────────────────────────────────────────────
# test_gapfill_string_mutation.ws
# Text is immutable in v2 (DEC-10, plan 5.5, T3.6): an index write into
# text raises TypeError on every path (a global, a local, a parameter, a
# property) and leaves the text unchanged. Build a new
# text instead.
# ──────────────────────────────────────────────────────

# ── A global (IndexSetGlobal) ──
s = "abcde"
begin
    s[0] = "X"
rescue e
    print e
end
assert s == "abcde"

# ── A parameter and a local (IndexSetLocal) ──
fun replace_first(s, c)
    t = s
    begin
        t[0] = c
    rescue e
        print "local: " + e
    end
    begin
        s[0] = c
    rescue e2
        print "parameter: " + e2
    end
    return t
end
assert replace_first("world", "W") == "world"

# ── A property (IndexSetProp) ──
class Label(text)
end
lbl = new Label("hello")
begin
    lbl.text[0] = "H"
rescue e3
    print "property: " + e3
end
assert lbl.text == "hello"

# ── Out of bounds is still TypeError: the write itself is not allowed ──
begin
    s[99] = "X"
rescue e5
    print "out of bounds: " + e5
end

# ── Building a new text instead ──
w = "hello"
w = "H" + w[1] + w[2] + w[3] + w[4]
assert w == "Hello"

# expect: Cannot assign to an index of text: text is immutable
# expect: local: Cannot assign to an index of text: text is immutable
# expect: parameter: Cannot assign to an index of text: text is immutable
# expect: property: Cannot assign to an index of text: text is immutable
# expect: out of bounds: Cannot assign to an index of text: text is immutable
