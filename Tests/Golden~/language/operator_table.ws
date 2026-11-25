# T3.10: the rows of the operator table (LANGUAGE.md, "Operators") that
# operator_types.ws does not cover. Type errors: operator_types.ws and
# static_operand_*.ws; limits: text_length_limit.ws and AllocationGuardTests;
# division and modulo by zero: errors/division_by_zero.ws, errors/modulo_by_zero.ws.

class Unit(name)
end
class Archer(name) : Unit(name)
end

# ── n * n, n / n, n % n ──
print 6 * 7                                   # expect: 42
print 7 / 2                                   # expect: 3.5
print 7 % 3                                   # expect: 1
print -7 % 3                                  # expect: -1
print 1.5 * 2                                 # expect: 3

# ── text * n: the count truncates toward zero; 0 or less gives "" ──
print "ab" * 2.9                              # expect: abab
print "[" + ("ab" * 0) + "]"                  # expect: []
print "[" + ("ab" * -3) + "]"                 # expect: []
print "[" + (-0.5 * "ab") + "]"               # expect: []

# ── !x: logical not of truthiness (null, false and 0 are falsy) ──
print !0                                      # expect: true
print !null                                   # expect: true
print !""                                     # expect: false
print ![]                                     # expect: false
print !1                                      # expect: false

# ── and / or: short-circuit, the deciding operand is the result ──
fun loud(v)
    print "evaluated {v}"
    return v
end
print 1 and 2                                 # expect: 2
print 0 and loud(1)                          # expect: 0
print null or "x"                             # expect: x
print "a" or loud(2)                         # expect: a
print false or loud(3)                       # expect: evaluated 3
                                              # expect: 3

# ── array << x appends and evaluates to the array ──
arr = []
same_arr = arr << 1
same_arr << [2]
print arr                                     # expect: [1, [2]]
print same_arr == arr                         # expect: true

# ── is / as ──
a = new Archer("robin")
print a is Unit                               # expect: true
print a is Archer                             # expect: true
print 5 is Unit                               # expect: false
print (a as Unit) == a                        # expect: true
print 5 as Unit                               # expect: null
