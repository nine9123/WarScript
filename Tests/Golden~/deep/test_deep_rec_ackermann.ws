# test_deep_rec_ackermann.ws — Ackermann function (small values)

fun ack [m, n]
    if m == 0
        return n + 1
    end
    if n == 0
        return ack [m - 1, 1]
    end
    return ack [m - 1, ack [m, n - 1]]
end
assert ack [0, 0] == 1
assert ack [1, 1] == 3
assert ack [2, 2] == 7
assert ack [3, 0] == 5
assert ack [3, 1] == 13
