# T3.6 (DEC-10, plan 5.5): arr{i} and text{i} need a numeric index, which
# truncates toward zero (D1); out of bounds is IndexError, any other index
# or target type is TypeError, text is immutable, and arrays never grow on
# an index write (use <<). The write opcodes reachable from source are
# covered: IndexSetGlobal (top level), IndexSetLocal (function) and
# IndexSetProp (property).

fun attempt [f]
    begin
        f []
    rescue e
        print e
    end
end

arr = {10, 20, 30}

# ── Reads ──
print arr{0}                                  # expect: 10
print arr{2.9}                                # expect: 30
print arr{-0.5}                               # expect: 10
print "abc"{1}                                # expect: b
attempt [fun [] global arr return arr{3} end]     # expect: Index 3 is out of bounds (length 3)
attempt [fun [] global arr return arr{-1} end]    # expect: Index -1 is out of bounds (length 3)
attempt [fun [] global arr return arr{"a"} end]   # expect: Index must be a number, not text
attempt [fun [] global arr return arr{null} end]  # expect: Index must be a number, not null
attempt [fun [] return "abc"{3} end]              # expect: Index 3 is out of bounds (length 3)
attempt [fun [] return ""{0} end]                 # expect: Index 0 is out of bounds (length 0)
attempt [fun [] n = 5 return n{0} end]            # expect: Cannot index number
attempt [fun [] n = null return n{0} end]         # expect: Cannot index null
attempt [fun [] t = true return t{0} end]         # expect: Cannot index logical

class Box [items]
    fun put [i, v]
        @items{i} = v
    end
end
b = new Box [{1, 2}]
attempt [fun [] global b return b{0} end]         # expect: Cannot index instance of Box

# ── Writes: a global ──
arr{1} = 21
print arr                                     # expect: [10, 21, 30]
begin
    arr{3} = 40
rescue e
    print e                                   # expect: Index 3 is out of bounds (length 3)
end
begin
    arr{"x"} = 40
rescue e
    print e                                   # expect: Index must be a number, not text
end
print arr                                     # expect: [10, 21, 30]
arr << 40
print arr{3}                                  # expect: 40

# ── Writes: a local ──
fun write_local [i]
    a = {1, 2}
    a{i} = 9
    return a
end
print write_local [1]                         # expect: [1, 9]
attempt [fun [] return write_local [2] end]       # expect: Index 2 is out of bounds (length 2)
fun write_number []
    n = 5
    n{0} = 1
end
attempt [fun [] write_number [] end]              # expect: Cannot index number

# ── Writes: an element of a nested array, through a variable ──
m = {{1, 2}, {3, 4}}
row = m{1}
row{0} = 33
print m                                       # expect: [[1, 2], [33, 4]]

# ── Writes: a property ──
b :: items{1} = 22
print b :: items                              # expect: [1, 22]
begin
    b :: items{2} = 0
rescue e
    print e                                   # expect: Index 2 is out of bounds (length 2)
end
b :: put [0, 11]
print b :: items                              # expect: [11, 22]
attempt [fun [] global b b :: put [5, 0] end]     # expect: Index 5 is out of bounds (length 2)
nothing = null
begin
    nothing :: items{0} = 0
rescue e
    print e                                   # expect: Cannot access property 'items' on non-class value
end

# ── Property writes on a non-instance ──
begin
    nothing :: items = 5
rescue e
    print e                                   # expect: Cannot set property 'items' on non-class value
end
