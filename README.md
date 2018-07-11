# GZipTest
This was an attempt at a test-project for Veeam software (failed :D)
# The comments I got were:
1.           Нет обработки ошибок в запускаемых потоках
2.            Не используются примитивы синхронизации для ожидания каких-либо действий (логика работает на Sleep’ах)
3.           Переоткрытие файла на каждое чтение\запись медленее, чем последовательное чтение\запись

# TODO
If you want to use this as a base for your test-project, what I would improve over this one. Or maybe I come back to this later, the task was pretty interesting actually.
1. Managing errors in threads, at the very least outofmemory for memorystreams
2. Removing all sleeps and synchronizing through a signal probably (signal when reading a chunk, waking up work threads with that)
3. Testing if synchronously reading as described is faster (which I doubt actually, but hey, testing doesn't hurt)
4. Automate testing with at least some respectable number of test-cases