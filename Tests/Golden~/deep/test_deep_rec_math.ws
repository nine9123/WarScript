# test_deep_rec_math.ws — GCD, LCM, digit sum, reverse number

fun gcd [a, b]
    if b == 0
        return a
    end
    return gcd [b, a % b]
end
assert gcd [12, 8] == 4
assert gcd [100, 75] == 25
assert gcd [17, 13] == 1
assert gcd [0, 5] == 5
assert gcd [7, 7] == 7
assert gcd [48, 18] == 6

fun lcm [a, b]
    return a * b / gcd [a, b]
end
assert lcm [4, 6] == 12
assert lcm [3, 7] == 21
assert lcm [12, 8] == 24

fun digit_sum [n]
    if n < 10
        return n
    end
    return n % 10 + digit_sum [floor [n / 10]]
end
assert digit_sum [0] == 0
assert digit_sum [5] == 5
assert digit_sum [123] == 6
assert digit_sum [999] == 27
assert digit_sum [10000] == 1

fun reverse_num [n, acc]
    if n == 0
        return acc
    end
    return reverse_num [floor [n / 10], acc * 10 + n % 10]
end
assert reverse_num [123, 0] == 321
assert reverse_num [1000, 0] == 1
assert reverse_num [5, 0] == 5
assert reverse_num [1234, 0] == 4321
