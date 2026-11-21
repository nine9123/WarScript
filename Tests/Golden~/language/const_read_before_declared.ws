# T3.8: reading a constant above its declaration is no write: it compiles.
fun limit []
    return LIMIT
end
const LIMIT = 3
enum Mode
    IDLE
    BUSY
end
total = 0
total += LIMIT
print limit []
print total + Mode :: BUSY
# expect: 3
# expect: 4
