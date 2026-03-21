# test_deep_algo_roman.ws — Roman numeral converter

fun to_roman [n]
    result = ""
    loop n >= 10
        result += "X"
        n -= 10
    end
    if n >= 9
        result += "IX"
        n -= 9
    end
    if n >= 5
        result += "V"
        n -= 5
    end
    if n >= 4
        result += "IV"
        n -= 4
    end
    loop n >= 1
        result += "I"
        n -= 1
    end
    return result
end

assert to_roman [1] == "I"
assert to_roman [3] == "III"
assert to_roman [4] == "IV"
assert to_roman [9] == "IX"
assert to_roman [14] == "XIV"
assert to_roman [27] == "XXVII"
assert to_roman [39] == "XXXIX"
