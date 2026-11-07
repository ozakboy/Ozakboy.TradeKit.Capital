// 不平行執行:ServeLogin 的測試會改本行程的環境變數。
// Not parallel: the ServeLogin tests change this process's environment variables.
[assembly: DoNotParallelize]
