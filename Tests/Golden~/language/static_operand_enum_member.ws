# T3.8 (PROP-28): a member (and `count`) of an enum declared at the top level
# is a number, `values`/`names` an array, `name [...]` a text.
enum Rank
    LOW
    HIGH
end
print Rank.name(0) - 1   # expect-compile-error: Cannot apply '-' to text and number
