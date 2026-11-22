# T3.10: the equality rules of LANGUAGE.md section 4 (plan 5.4) that
# operator_types.ws (different types never equal, null == null) and
# instance_identity_diamond.ws (an instance equals itself seen through a cast)
# do not cover. Cyclic arrays: value_depth.ws.

# ── numbers compare by their raw fixed-point bits ──
print 0.5 == 1 / 2                            # expect: true
print 1 / 3 * 3 == 1                          # expect: false
print 1 / 3 * 3 != 1                          # expect: true

# ── logical values by value, text ordinally ──
print true == true                            # expect: true
print true != false                           # expect: true
print "a" == "A"                              # expect: false
print "ab" == "a" + "b"                       # expect: true

# ── arrays structurally, element by element ──
print {1, {2, "x"}} == {1, {2, "x"}}          # expect: true
print {1, {2, "x"}} != {1, {2, "y"}}          # expect: true
print {1, 2} == {1, 2, 3}                     # expect: false
print {} == {}                                # expect: true

# ── instances by identity: equal fields are not enough ──
class P [x, y]
end
p = new P [1, 2]
q = new P [1, 2]
print p == p                                  # expect: true
print p == q                                  # expect: false
print p != q                                  # expect: true
print {p} == {p}                              # expect: true
print {p} == {q}                              # expect: false

# ── function values: the same lambda is equal to itself, a second copy is not ──
f = fun [x] return x end
g = fun [x] return x end
print f == f                                  # expect: true
print f == g                                  # expect: false
fs = {}
loop i in 0..2
    fs << fun [x] return x end
end
print fs{0} == fs{1}                          # expect: true
