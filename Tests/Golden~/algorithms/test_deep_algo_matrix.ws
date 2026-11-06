# test_deep_algo_matrix.ws — 2x2 matrix multiplication
# (builds result step by step to reduce peak memory)

fun mat2_mul [a, b]
    a0 = a{0}
    a1 = a{1}
    b0 = b{0}
    b1 = b{1}
    r00 = a0{0} * b0{0} + a0{1} * b1{0}
    r01 = a0{0} * b0{1} + a0{1} * b1{1}
    r10 = a1{0} * b0{0} + a1{1} * b1{0}
    r11 = a1{0} * b0{1} + a1{1} * b1{1}
    row0 = {r00, r01}
    row1 = {r10, r11}
    return {row0, row1}
end

m1 = {{1, 2}, {3, 4}}
m2 = {{5, 6}, {7, 8}}
result = mat2_mul [m1, m2]
assert result{0} == {19, 22}
assert result{1} == {43, 50}

id = {{1, 0}, {0, 1}}
m3 = {{3, 4}, {5, 6}}
result2 = mat2_mul [id, m3]
assert result2{0} == {3, 4}
assert result2{1} == {5, 6}
