# Generate-SkComInterop.ps1 的規格:每個輸出檔有哪些 CLSID / IID、介面、事件、結構,以及哪些方法給真簽章。
# Real:方法名稱 = 摘要(空字串 = 不寫摘要);不在 Real 的方法一律產生 Unused 佔位。
# OutOverride:型別庫是 [in,out],但既有程式已用 out 對真 SKCOM 實測過的成員,保留 out 簽章(serve 的行為不變)。
# Forbidden:Real 出現這些字樣(不分大小寫)產生器就中止。手冊章節號指《策略王COM元件使用說明_V2.13.59》。
@{
    TypeLibVersion = 'SKCOM 2.13.59'

    Files = @(
        @{
            Name = 'SkComInterop.cs'
            Header = @(
                'SKCenterLib(登入、環境)、SKReplyLib 的公告事件、SKOSQuoteLib(海外期貨 / 外匯報價)。'
            )
            Ids = @{
                Name = 'SkComIds'
                Summary = 'SKCOM 的 CLSID 與事件介面 IID。SKCOM class and event interface identifiers.'
                Entries = @(
                    @{ Field = 'CenterLibClsid'; Type = 'SKCenterLibClass'; Summary = 'SKCenterLib 的 CLSID(SKCenterLibClass)。' }
                    @{ Field = 'ReplyLibClsid'; Type = 'SKReplyLibClass'; Summary = 'SKReplyLib 的 CLSID(SKReplyLibClass)。' }
                    @{ Field = 'OsQuoteLibClsid'; Type = 'SKOSQuoteLibClass'; Summary = 'SKOSQuoteLib 的 CLSID(SKOSQuoteLibClass)。' }
                    @{ Field = 'ReplyLibEventsIid'; Type = '_ISKReplyLibEvents'; Summary = '_ISKReplyLibEvents 的 IID。' }
                    @{ Field = 'OsQuoteLibEventsIid'; Type = '_ISKOSQuoteLibEvents'; Summary = '_ISKOSQuoteLibEvents 的 IID。' }
                )
            }
            Types = @(
                @{
                    Kind = 'Dual'; TypeLib = 'ISKCenterLib'; Name = 'ISKCenterLib'; Prefix = 'SKCenterLib_'
                    Summary = 'SKCenterLib:登入與環境設定。'
                    Real = @{
                        'SKCenterLib_Login' = '登入(手冊 4-1);帳密錯誤不得自動重試,錯太多次會鎖帳號。'
                        'SKCenterLib_GetReturnCodeMessage' = '代碼對應的訊息。'
                        'SKCenterLib_SetLogPath' = 'SKCOM 自己的紀錄目錄。'
                        'SKCenterLib_GetLastLogInfo' = '最後一筆紀錄(登入失敗時看原因;可能含帳號,寫出前要遮)。'
                        'SKCenterLib_GetSKAPIVersionAndBit' = 'SKCOM 版本與位元數。'
                    }
                }
                @{
                    Kind = 'Events'; TypeLib = '_ISKReplyLibEvents'; Name = 'ISKReplyLibEvents'
                    Summary = '_ISKReplyLibEvents:只接 OnReplyMessage。SKReplyLib 物件本身只用來掛這個事件(手冊 3-2:登入前必要),不呼叫它的方法。'
                    Members = @{
                        'OnReplyMessage' = '公告。登入前必須註冊,並把 <paramref name="sConfirmCode"/> 設成 -1(手冊 4-3-e)。'
                    }
                }
                @{
                    Kind = 'Dual'; TypeLib = 'ISKOSQuoteLib'; Name = 'ISKOSQuoteLib'; Prefix = 'SKOSQuoteLib_'
                    Summary = 'SKOSQuoteLib:海外期貨 / 外匯報價。'
                    Real = @{
                        'SKOSQuoteLib_LeaveMonitor' = ''
                        'SKOSQuoteLib_RequestTicks' = '訂閱成交明細(一次一檔,頁碼 -1 由函式庫指定)。'
                        'SKOSQuoteLib_RequestOverseaProducts' = '商品清單,以 ## 結尾(2026-11-06 實測)。'
                        'SKOSQuoteLib_RequestServerTime' = ''
                        'SKOSQuoteLib_RequestStocks' = '訂閱報價(代碼以 # 串接,頁碼 -1;2026-11-06 實測)。'
                        'SKOSQuoteLib_IsConnected' = ''
                        'SKOSQuoteLib_GetOverseaProductDetail' = '商品明細(sType 1),以 ## 結尾。'
                        'SKOSQuoteLib_RequestMarketDepth' = '訂閱十檔(OnNotifyBest10NineDigitLONG;格式待 probe-full 實測)。'
                        'SKOSQuoteLib_GetQuoteStatus' = ''
                        'SKOSQuoteLib_RequestKLineByDate' = '依日期查歷史 K 線。'
                        'SKOSQuoteLib_EnterMonitorLONG' = ''
                        'SKOSQuoteLib_GetStockByIndexNineDigitLONG' = ''
                        'SKOSQuoteLib_GetStockByNoNineDigitLONG' = ''
                    }
                    OutOverride = @('SKOSQuoteLib_GetQuoteStatus', 'SKOSQuoteLib_GetStockByIndexNineDigitLONG', 'SKOSQuoteLib_GetStockByNoNineDigitLONG')
                }
                @{
                    Kind = 'Events'; TypeLib = '_ISKOSQuoteLibEvents'; Name = 'ISKOSQuoteLibEvents'
                    Summary = '_ISKOSQuoteLibEvents:海外報價事件(只宣告用得到的)。'
                    Members = @{
                        'OnConnect' = '連線狀態:3001 連線、3002 斷線(實測只有 3001,沒有 3003)。'
                        'OnOverseaProducts' = '海外商品清單(每筆一則)。'
                        'OnKLineData' = '歷史 K 線(每根一則)。'
                        'OnNotifyServerTime' = '報價主機時間。'
                        'OnOverseaProductsDetail' = '海外商品明細(含商品種類)。'
                        'OnNotifyQuoteLONG' = '報價異動,再以 GetStockByIndexNineDigitLONG 取值。'
                        'OnNotifyTicksNineDigitLONG' = '即時成交(九位小數)。'
                        'OnNotifyHistoryTicksNineDigitLONG' = '訂閱當下回補的當日歷史成交(九位小數)。'
                        'OnNotifyBest5NineDigitLONG' = '最佳五檔(九位小數;格式待 probe-full 實測)。'
                        'OnNotifyBest10NineDigitLONG' = '最佳十檔(RequestMarketDepth;格式待 probe-full 實測)。'
                    }
                }
                @{
                    Kind = 'Struct'; TypeLib = 'SKFOREIGN_9LONG'
                    Summary = '海外報價商品物件(九位小數、LONG 索引)。'
                }
            )
        }

        @{
            Name = 'SkComDomesticInterop.cs'
            Header = @(
                'SKQuoteLib(國內證券 / 期貨 / 選擇權報價)。型別庫的 DispId 在第 3 格之後跳過 4,vtable 看的是順序不是 DispId。'
            )
            Ids = @{
                Name = 'SkComDomesticIds'
                Summary = 'SKQuoteLib 的 CLSID 與事件介面 IID。SKQuoteLib class and event interface identifiers.'
                Entries = @(
                    @{ Field = 'QuoteLibClsid'; Type = 'SKQuoteLibClass'; Summary = 'SKQuoteLib 的 CLSID(SKQuoteLibClass)。' }
                    @{ Field = 'QuoteLibEventsIid'; Type = '_ISKQuoteLibEvents'; Summary = '_ISKQuoteLibEvents 的 IID。' }
                )
            }
            Types = @(
                @{
                    Kind = 'Dual'; TypeLib = 'ISKQuoteLib'; Name = 'ISKQuoteLib'; Prefix = 'SKQuoteLib_'
                    Summary = 'SKQuoteLib:國內證券 / 期權報價。'
                    Real = @{
                        'SKQuoteLib_LeaveMonitor' = ''
                        'SKQuoteLib_RequestStocks' = '訂閱報價(手冊 4-4-2:代碼以逗號分隔、psPageNo 自 V2.13.54 起固定帶 1;一個物件只能有一組即時報價訂閱)。'
                        'SKQuoteLib_RequestTicks' = '訂閱成交明細、五檔、即時分 K(手冊 4-4-3:psPageNo 從 0 開始,一個 Page 一檔)。'
                        'SKQuoteLib_RequestServerTime' = ''
                        'SKQuoteLib_RequestStockList' = '商品清單(手冊 4-4-17:上市 0、上櫃 1、期貨 2、選擇權 3…),以 ## 結尾。'
                        'SKQuoteLib_IsConnected' = ''
                        'SKQuoteLib_GetQuoteStatus' = ''
                        'SKQuoteLib_RequestKLineAMByDate' = '依日期與盤別查歷史 K 線(手冊 4-4-24)。'
                        'SKQuoteLib_EnterMonitorLONG' = ''
                        'SKQuoteLib_GetStockByIndexLONG' = '依市場別與索引取報價(手冊 4-4-26;不要在 OnNotifyHistoryTicksLONG 裡呼叫)。'
                        'SKQuoteLib_GetStockByNoLONG' = '依代碼取報價(手冊 4-4-27;沒訂報價時只有基本資料)。'
                        'SKQuoteLib_GetStockByMarketAndNo' = '依市場別與代碼取報價(手冊 4-4-33)。'
                        'SKQuoteLib_EnterMonitorLONGByMarket' = ''
                    }
                    OutOverride = @('SKQuoteLib_GetQuoteStatus')
                }
                @{
                    Kind = 'Events'; TypeLib = '_ISKQuoteLibEvents'; Name = 'ISKQuoteLibEvents'
                    Summary = '_ISKQuoteLibEvents:國內報價事件(只宣告用得到的)。'
                    Members = @{
                        'OnConnection' = '連線狀態:nKind 3001 連線、3002 斷線、3003 商品基本資料下載完成(手冊 4-4-a)。'
                        'OnNotifyKLineData' = '歷史 K 線(每根一則,手冊 4-4-b)。'
                        'OnNotifyServerTime' = '報價主機時間。'
                        'OnNotifyStockList' = '商品清單(每個市場分多則,手冊 4-4-h)。'
                        'OnNotifyQuoteLONG' = '報價異動,再以 GetStockByIndexLONG 取值(手冊 4-4-k)。'
                        'OnNotifyHistoryTicksLONG' = '訂閱當下回補的當日成交(手冊 4-4-l)。'
                        'OnNotifyTicksLONG' = '即時成交(手冊 4-4-m)。'
                        'OnNotifyBest5LONG' = '最佳五檔(手冊 4-4-n)。'
                        'OnKLineComplete' = '歷史 K 線全部送完(結尾字串 "##",手冊 4-4-t)。'
                        'OnNotifyLiveKLineData' = '即時分 K(手冊 4-4-u;nType 0 清盤、1 分 K 含當日回補、2 該分鐘每筆)。'
                    }
                }
                @{
                    Kind = 'Struct'; TypeLib = 'SKSTOCKLONG'
                    Summary = '國內報價商品物件(LONG 索引)。'
                }
            )
        }

        @{
            Name = 'SkComOrderInterop.cs'
            Header = @(
                'SKOrderLib(帳務查詢)。'
                '安全規則:這個套件只讀(報價與帳務查詢),而且群益已經沒有模擬平台,呼叫錯一個方法就是真單、真金流。'
                '所以 ISKOrderLib 上所有會改變委託、部位或資金的方法(送單、刪單、減量、改價、停損 / MIT / OCO / 智慧單、代理下單、'
                'WithDraw、CapitalPayWithDraw、SpecialRequest、UnlockOrder、SetMaxQty、SetMaxCount…)一律只能是 Unused 佔位,'
                '不得有可呼叫的真簽章。產生器對 Forbidden 字樣硬擋,單元測試 SkComOrderInteropSafetyTests 以反射再擋一次。'
                'Safety rule: this package is read-only and Capital has no simulation platform any more, so every method that could'
                'change an order, a position or funds stays an Unused placeholder. The generator and a reflection test both enforce it.'
            )
            Ids = @{
                Name = 'SkComOrderIds'
                Summary = 'SKOrderLib 的 CLSID 與事件介面 IID。SKOrderLib class and event interface identifiers.'
                Entries = @(
                    @{ Field = 'OrderLibClsid'; Type = 'SKOrderLibClass'; Summary = 'SKOrderLib 的 CLSID(SKOrderLibClass)。' }
                    @{ Field = 'OrderLibEventsIid'; Type = '_ISKOrderLibEvents'; Summary = '_ISKOrderLibEvents 的 IID。' }
                )
            }
            Types = @(
                @{
                    Kind = 'Dual'; TypeLib = 'ISKOrderLib'; Name = 'ISKOrderLib'; Prefix = 'SKOrderLib_'
                    Summary = 'SKOrderLib:只開放帳務查詢;所有下單 / 刪改單 / 出入金方法都是 Unused 佔位(見檔頭安全規則)。'
                    Real = @{
                        'SKOrderLib_Initialize' = '下單物件初始化(手冊 4-2-1;登入後、查詢前)。'
                        'GetUserAccount' = '取回可交易帳號,由 OnAccount 回傳(手冊 4-2-2)。'
                        'ReadCertByID' = '讀取憑證(手冊 4-2-3)。'
                        'GetFutureRights' = '國內期貨權益數(手冊 4-2-41;sCoinType 0 全幣別、1 基幣台幣、2 人民幣),由 OnFutureRights / OnFutureRightsStatus 回傳。'
                        'GetRequestOverSeaFutureRight' = '海外期貨權益數(手冊 4-2-45),由 OnOverSeaFutureRight / OnOverseaFutureRightsStatus 回傳。'
                        'GetOverseaFutureOpenInterestGW' = '海外期貨未平倉(手冊 4-2-76;nFormat 1 彙總、2 明細),由 OnOFOpenInterestGWReport / OnOverseaFutureOpenInterestGWStatus 回傳。'
                        'GetOpenInterestGW' = '國內期貨未平倉(手冊 4-2-89;nFormat 手冊只列 1),由 OnOpenInterest / OnOpenInterestJson / OnOpenInterestGWStatus 回傳。'
                        'GetOrderReport' = '委託回報查詢(手冊 4-2-97;阻塞式、每次間隔 5 秒、只有國內證期選;nFormat 1 全部 … 9 預約),直接回字串。'
                        'GetFulfillReport' = '成交回報查詢(手冊 4-2-98;阻塞式、每次間隔 5 秒;nFormat 1 完整 … 5 T+1),直接回字串。'
                        'GetOFOpenInterestWithDetails' = '海外期貨未平倉明細(手冊沒有章節,型別庫才有),由 OnOverseaFutureOpenInterestWithDetails 回傳。'
                    }
                    Forbidden = @('Send', 'Cancel', 'Decrease', 'Correct', 'Alter', 'WithDraw', 'Offset', 'Cover', 'Assemble', 'Disassemble', 'Basket', 'Proxy', 'Unlock', 'SetMax', 'Special', 'Duplex', 'Strategy')
                }
                @{
                    Kind = 'Events'; TypeLib = '_ISKOrderLibEvents'; Name = 'ISKOrderLibEvents'
                    Summary = '_ISKOrderLibEvents:帳務查詢的回傳事件(只宣告查詢用得到的;委託類事件不接)。'
                    Members = @{
                        'OnAccount' = '帳號(手冊 4-2-a):市場,分公司代碼,分公司,帳號,身份證字號,姓名;期貨的分公司代碼是 Broker id。'
                        'OnOpenInterest' = '國內期貨未平倉(手冊 4-2-d),以 ## 結尾;查無資料回 001,查無資料,帳號。'
                        'OnOverseaFutureOpenInterest' = '海外期貨未平倉(舊版查詢,手冊 4-2-e)。'
                        'OnFutureRights' = '國內期貨權益數(手冊 4-2-i),以 ## 結尾。'
                        'OnOverSeaFutureRight' = '海外期貨權益數(手冊 4-2-k)。'
                        'OnOFOpenInterestGWReport' = '海外期貨未平倉彙總 / 明細(手冊 4-2-q):第一筆 @@,代碼,訊息,以 ## 結尾。'
                        'OnOpenInterestGWStatus' = '國內未平倉查詢狀態(手冊 4-2-t;0 成功、1 失敗)。'
                        'OnOverseaFutureOpenInterestGWStatus' = '海外未平倉查詢狀態(手冊 4-2-u)。'
                        'OnFutureRightsStatus' = '國內權益數查詢狀態(手冊 4-2-v)。'
                        'OnOverseaFutureRightsStatus' = '海外權益數查詢狀態(手冊 4-2-w)。'
                        'OnOpenInterestJson' = '國內未平倉一次全部(JSON 陣列,手冊 4-2-x)。'
                        'OnOverseaFutureOpenInterestWithDetails' = '海外未平倉明細(GetOFOpenInterestWithDetails;手冊沒有章節)。'
                        'OnOpenInterestWithDetails' = '國內未平倉明細(手冊沒有章節,觸發來源待實測)。'
                    }
                }
            )
        }

        @{
            Name = 'SkComOverseasOptionInterop.cs'
            Header = @(
                'SKOOQuoteLib(海外選擇權報價)。V2.13.59 手冊沒有這個物件的章節,參數規則比照 SKOSQuoteLib(頁碼 -1、代碼以 # 串接),待 probe-full 實測。'
            )
            Ids = @{
                Name = 'SkComOverseasOptionIds'
                Summary = 'SKOOQuoteLib 的 CLSID 與事件介面 IID。SKOOQuoteLib class and event interface identifiers.'
                Entries = @(
                    @{ Field = 'OoQuoteLibClsid'; Type = 'SKOOQuoteLibClass'; Summary = 'SKOOQuoteLib 的 CLSID(SKOOQuoteLibClass)。' }
                    @{ Field = 'OoQuoteLibEventsIid'; Type = '_ISKOOQuoteLibEvents'; Summary = '_ISKOOQuoteLibEvents 的 IID。' }
                )
            }
            Types = @(
                @{
                    Kind = 'Dual'; TypeLib = 'ISKOOQuoteLib'; Name = 'ISKOOQuoteLib'; Prefix = 'SKOOQuoteLib_'
                    Summary = 'SKOOQuoteLib:海外選擇權報價。'
                    Real = @{
                        'SKOOQuoteLib_LeaveMonitor' = ''
                        'SKOOQuoteLib_RequestProducts' = '商品清單,由 OnProducts 回傳(結尾標記待實測)。'
                        'SKOOQuoteLib_RequestStocks' = '訂閱報價。'
                        'SKOOQuoteLib_RequestTicks' = '訂閱成交明細與五檔。'
                        'SKOOQuoteLib_IsConnected' = ''
                        'SKOOQuoteLib_RequestMarketDepth' = '訂閱十檔。'
                        'SKOOQuoteLib_EnterMonitorLONG' = ''
                        'SKOOQuoteLib_GetStockByIndexLONG' = ''
                        'SKOOQuoteLib_GetStockByNoLONG' = ''
                    }
                }
                @{
                    Kind = 'Events'; TypeLib = '_ISKOOQuoteLibEvents'; Name = 'ISKOOQuoteLibEvents'
                    Summary = '_ISKOOQuoteLibEvents:海外選擇權報價事件(只宣告用得到的)。'
                    Members = @{
                        'OnConnect' = '連線狀態(代碼待實測)。'
                        'OnProducts' = '商品清單一筆。'
                        'OnNotifyQuoteLONG' = '報價異動,再以 GetStockByIndexLONG 取值。'
                        'OnNotifyTicksLONG' = '即時成交。'
                        'OnNotifyHistoryTicksLONG' = '訂閱當下回補的當日成交。'
                        'OnNotifyBest5LONG' = '最佳五檔。'
                        'OnNotifyBest10LONG' = '最佳十檔(RequestMarketDepth)。'
                    }
                }
                @{
                    Kind = 'Struct'; TypeLib = 'SKFOREIGNLONG'
                    Summary = '海外選擇權報價商品物件(LONG 索引)。'
                }
            )
        }
    )
}
