# ──────────────────────────────────────────────────────
# test_math_library.ws
# Covers: pow, sqrt, floor, ceil, round, abs, min, max,
#         clamp, sign, lerp — all native math functions
# Requires MathLibrary to be registered
# ──────────────────────────────────────────────────────

# ── pow ──
assert pow [2, 0] == 1
assert pow [2, 1] == 2
assert pow [2, 10] == 1024
assert pow [3, 3] == 27
assert pow [5, 2] == 25
assert pow [10, 0] == 1
assert pow [0, 5] == 0

# ── sqrt ──
assert sqrt [4] == 2
assert sqrt [9] == 3
assert sqrt [16] == 4
assert sqrt [100] == 10
assert sqrt [0] == 0
assert sqrt [1] == 1

# ── floor ──
assert floor [3.7] == 3
assert floor [3.0] == 3
assert floor [3.1] == 3
assert floor [3.9] == 3
assert floor [0.5] == 0
assert floor [-0.5] == -1
assert floor [-3.7] == -4
assert floor [0] == 0

# ── ceil ──
assert ceil [3.1] == 4
assert ceil [3.0] == 3
assert ceil [3.9] == 4
assert ceil [0.1] == 1
assert ceil [-0.5] == 0
assert ceil [-3.1] == -3
assert ceil [0] == 0

# ── round ──
assert round [3.4] == 3
assert round [3.5] == 4
assert round [3.6] == 4
assert round [-3.4] == -3
assert round [-3.6] == -4
assert round [0] == 0
assert round [0.5] == 0       # banker's rounding
assert round [1.5] == 2

# ── abs ──
assert abs [5] == 5
assert abs [-5] == 5
assert abs [0] == 0
assert abs [-0] == 0
assert abs [3.14] == 3.14
assert abs [-3.14] == 3.14

# ── min ──
assert min [3, 7] == 3
assert min [7, 3] == 3
assert min [5, 5] == 5
assert min [-1, 1] == -1
assert min [0, 0] == 0

# ── max ──
assert max [3, 7] == 7
assert max [7, 3] == 7
assert max [5, 5] == 5
assert max [-1, 1] == 1
assert max [0, 0] == 0

# ── clamp ──
assert clamp [5, 0, 10] == 5
assert clamp [-5, 0, 10] == 0
assert clamp [15, 0, 10] == 10
assert clamp [0, 0, 10] == 0
assert clamp [10, 0, 10] == 10
assert clamp [5, 5, 5] == 5

# ── sign ──
assert sign [10] == 1
assert sign [-10] == -1
assert sign [0] == 0
assert sign [0.001] == 1
assert sign [-0.001] == -1

# ── lerp ──
assert lerp [0, 10, 0] == 0
assert lerp [0, 10, 1] == 10
assert lerp [0, 10, 0.5] == 5
assert lerp [0, 10, 0.25] == 2.5
assert lerp [10, 20, 0.5] == 15
assert lerp [-10, 10, 0.5] == 0

# ── Combinations of math functions ──
assert floor [sqrt [10]] == 3
assert ceil [sqrt [10]] == 4
assert abs [min [-5, -10]] == 10
assert max [abs [-5], abs [-3]] == 5
assert clamp [floor [3.7], 0, 5] == 3
assert pow [abs [-2], 3] == 8

# ── Math in loop ──
factorials = {}
loop i in 0..6
    if i == 0
        factorials << 1
    else
        factorials << factorials{i - 1} * i
    end
end
assert factorials == {1, 1, 2, 6, 24, 120}

# ── Math with class ──
class Circle [radius]
    fun area []
        return 3.14159 * pow[radius, 2]
    end
    fun circumference []
        return 2 * 3.14159 * radius
    end
end

c = new Circle [5]
assert floor [c :: area []] == 78
assert floor [c :: circumference []] == 31

# ── Distance calculation ──
fun distance [x1, y1, x2, y2]
    dx = x2 - x1
    dy = y2 - y1
    return sqrt [pow [dx, 2] + pow [dy, 2]]
end
assert distance [0, 0, 3, 4] == 5
assert distance [0, 0, 0, 0] == 0
assert distance [1, 1, 4, 5] == 5
