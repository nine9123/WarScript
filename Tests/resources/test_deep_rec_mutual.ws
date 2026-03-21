# test_deep_rec_mutual.ws — Mutual recursion + string repeat

fun is_even_m [n]
    if n == 0
        return true
    end
    return is_odd_m [n - 1]
end
fun is_odd_m [n]
    if n == 0
        return false
    end
    return is_even_m [n - 1]
end
assert is_even_m [0]
assert !is_even_m [1]
assert is_even_m [10]
assert is_odd_m [7]
assert !is_odd_m [4]

fun str_repeat [s, n]
    if n <= 0
        return ""
    end
    return s + str_repeat [s, n - 1]
end
assert str_repeat ["ab", 3] == "ababab"
assert str_repeat ["x", 0] == ""
assert str_repeat ["ha", 1] == "ha"
