﻿using DotNet.Utilities;
using HtmlAgilityPack;
using MongoDB.Bson;
using MongoDB.Driver.Builders;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Yinhe.ProcessingCenter;
using Yinhe.ProcessingCenter.DataRule;

namespace SimpleCrawler.Demo
{
    /// <summary>
    /// 用于城市与区域代码初始化
    /// </summary>
    public class FangDetailCrawler_JiangYin : ISimpleCrawler
    {

         
        DataOperation dataop = null;
        private CrawlSettings Settings = null;
        /// <summary>
        /// The filter.
        /// 关于使用 Bloom 算法去除重复 URL：
        ///  www.hhcool.com/cool62061/1.html?s=10&d=0
        /// 结束http://www.hhcool.com/cool62061/253.html?s=10&d=0
        /// </summary>
        private BloomFilter<string> filter;
        private BloomFilter<string> idFilter = new BloomFilter<string>(8000000);
        private const string _DataTableName = "FangCrawler.JiangYin";//存储的数据库表名

        /// <summary>
        /// 项目名
        /// </summary>
        public string DataTableName
        {
            get { return _DataTableName + "_Project"; }

        }
        /// <summary>
        /// 房屋名
        /// </summary>
        public string DataTableNameHouse
        {
            get { return _DataTableName + "_House"; }

        }
        /// <summary>
        /// 返回
        /// </summary>
        public string DataTableNameURL
        {
            get { return _DataTableName + "URL"; }

        }

        /// <summary>
        /// 区域
        /// </summary>
        public string DataTableNameRegion
        {
            get { return _DataTableName + "_Region"; }

        }
        /// <summary>
        /// 五类类型
        /// </summary>
        public string DataTableNameType
        {
            get { return _DataTableName + "_Type"; }

        }



        /// <summary>
        /// 谁的那个
        /// </summary>
        /// <param name="_Settings"></param>
        /// <param name="filter"></param>
        public FangDetailCrawler_JiangYin(CrawlSettings _Settings, BloomFilter<string> _filter, DataOperation _dataop)
        {
            Settings = _Settings; filter = _filter; dataop = _dataop;
        }


        List<BsonDocument> projectList = new List<BsonDocument>();
        public void SettingInit()//进行Settings.SeedsAddress Settings.HrefKeywords urlFilterKeyWord 基础设定
        {
            //webClient.DownloadFileCompleted += WebClient_DownloadFileCompleted;
            //种子地址需要加布隆过滤

            //Settings.Depth = 4;
            //代理ip模式
            Settings.IPProxyList = new List<IPProxy>();
            Settings.IgnoreSucceedUrlToDB = true;//不添加地址到数据库
            Settings.MaxReTryTimes = 20;
            Settings.ThreadCount =10;
            Settings.Accept = "text/html,application/xhtml+xml,application/xml;q=0.9,image/webp,image/apng,*/*;q=0.8";
            Settings.ContentType = "application/x-www-form-urlencoded";
            Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/66.0.3359.181 Safari/537.36";
            Settings.HeadSetDic = new Dictionary<string, string>();
            Settings.HeadSetDic.Add("Accept-Encoding", "gzip, deflate");
            Settings.SimulateCookies = "UM_distinctid=163c909dac3a0b-0a58055a844938-737356c-13c680-163c909dac46fa; JSESSIONID=0000uekkAKP_Q0hcDP33LrLYCY5:-1; CNZZDATA4237675=cnzz_eid%3D1374545162-1528085523-null%26ntime%3D1528091007";
            Settings.Referer = "http://www.jyfcc.com.cn/PreSellCert_Detail.do?pscid=101074";
            Console.WriteLine("正在获取已存在的url数据");
            //布隆url初始化,防止重复读取url
            Console.WriteLine("正在初始化选择url队列");
          
            projectList = dataop.FindAllByQuery(DataTableName, Query.NE("isUpdate", 1)).ToList();
            //iCPH
            foreach (var proj in projectList)
            {
                var mhUrl = string.Format("http://www.jyfcc.com.cn/ifrm_House_List.do?pscid={0}", proj.Text("projId"));
                if (!filter.Contains(mhUrl))//具体页面
                {
                    filter.Add(mhUrl);
                    UrlQueue.Instance.EnQueue(new UrlInfo(mhUrl) { Depth = 1, UniqueKey=proj.Text("projId") });
                }
            }
           // UrlQueue.Instance.EnQueue(new UrlInfo("http://www.hhcool.com/cool286073/1.html?s=11&d=0"+"&checkPageCount=1") { Depth = 1 });
            
                 //Settings.SeedsAddress.Add(string.Format("http://fdc.fang.com/data/land/CitySelect.aspx"));
            Settings.RegularFilterExpressions.Add("XXX");//不添加其他
            if (SimulateLogin())
            {
                //  Console.WriteLine("zluckymn模拟登陆成功");
            }
            else
            {
                Console.WriteLine("初始化失败");
            }

        }

        private void WebClient_DownloadFileCompleted(object sender, AsyncCompletedEventArgs e)
        {
            Console.WriteLine("下载成功");
        }

        public static object lockThis = new object();
        public static List<Task> allTask = new List<Task>();
        /// <summary>
        /// 数据接收处理，失败后抛出NullReferenceException异常，主线程会进行捕获
        /// cool62061/1.html?s=10&d=0
        /// 
        /// </summary>
        /// <param name="args">url参数</param>
        public void DataReceive(DataReceivedEventArgs args)
        {
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(args.Html);
            var root = htmlDoc.DocumentNode;
            var contentTableNode = htmlDoc.GetElementbyId("content");
            var projId = args.urlInfo.UniqueKey;
            var page = GetUrlParam(args.Url, "page");
            //获取页数
            if (contentTableNode!=null)
            {
                var add=0;
                var update = 0;
                var trNodeList = contentTableNode.SelectNodes("//table/tr").Where(c=>!c.InnerText.Contains("楼盘表"));
                if (trNodeList != null)
                {
                    foreach (var trNode in trNodeList)
                    {
                        if (trNode.InnerText.Contains("房屋代码")|| trNode.InnerText.Contains("面积"))
                        {
                            continue;
                        }
                        if (trNode.InnerText.Contains("页/共") )
                        {
                            continue;
                        }
                        var tdList= trNode.SelectNodes("./td");
                        if (tdList == null) continue;
                        if (tdList.Count == 9)
                        {
                            var codeNode = tdList[0];
                            var code = GetInnerText(codeNode).Trim();
                            var aNode = codeNode.SelectSingleNode("./a");
                            if (aNode==null|| aNode.Attributes["href"] == null) continue;
                            var href = aNode.Attributes["href"].Value;
                            var houseId = GetGuidFromUrl(href);
                            var buildNO = GetInnerText(tdList[1]).Trim();
                            var unitNO = GetInnerText(tdList[2]).Trim();
                            var roomNo = GetInnerText(tdList[3]).Trim();
                            var purpose = GetInnerText(tdList[4]).Trim();
                            var saleArea = GetInnerText(tdList[5]).Trim();
                            var drawStatus = GetInnerText(tdList[6]).Trim();
                            var price = GetInnerText(tdList[7]).Trim();
                            var saleStatus = GetInnerText(tdList[8]).Trim();
                            var houseDoc = new BsonDocument();
                            houseDoc.Add("houseId", houseId);
                            houseDoc.Add("code", code);
                            houseDoc.Add("buildNO", buildNO);
                            houseDoc.Add("unitNO", unitNO);
                            houseDoc.Add("roomNo", roomNo);
                            houseDoc.Add("purpose", purpose);
                            houseDoc.Add("saleArea", saleArea);
                            houseDoc.Add("drawStatus", drawStatus);
                            houseDoc.Add("price", price);
                            houseDoc.Add("saleStatusName", saleStatus);
                            houseDoc.Add("projId", projId);
                            if (saleStatus == "待售")
                            {
                                houseDoc.Set("saleStatus", "0");
                            }
                            if (saleStatus == "已售")
                            {
                                houseDoc.Set("saleStatus", "1");
                            }
                            var curHouseObj = this.dataop.FindOneByQuery(this.DataTableNameHouse, Query.EQ("houseId", houseId));
                            if (!idFilter.Contains(houseId) && curHouseObj==null)
                            {
                                
                                add++;
                                DBChangeQueue.Instance.EnQueue(new StorageData() { Document = houseDoc, Name = DataTableNameHouse, Type = StorageType.Insert });
                            }
                            else
                            {
                                if (string.IsNullOrEmpty(curHouseObj.Text("projId")))
                                {
                                    var updateDoc = new BsonDocument().Add("projId", projId);
                                    DBChangeQueue.Instance.EnQueue(new StorageData() { Document = updateDoc, Query = Query.EQ("houseId", houseId), Name = DataTableNameHouse, Type = StorageType.Update });
                                }
                                update++;
                            }
                           
                        }
                        else {
                            Console.WriteLine("列Td格式大于9个");
                        }
                       
                    }

                    Console.WriteLine("新增{0}更新:{1}", add,update);
                    //获取其他分页
                    if (string.IsNullOrEmpty(page) || page == "1")
                    {
                        var pageSpan = contentTableNode.SelectSingleNode("//span[@class='pagelist_last']/a");
                        if (pageSpan != null && pageSpan.Attributes["href"] != null)
                        {
                            var num = GetUrlParam(pageSpan.Attributes["href"].Value, "page");
                            var maxPageNum = 0;
                            if (int.TryParse(num, out maxPageNum))
                            {
                                Console.WriteLine("获取页数{0}", maxPageNum);
                                for (var i = 2; i <= maxPageNum; i++)
                                {
                                    var url = string.Format("{0}&page={1}", args.Url, i);
                                    if (!filter.Contains(url))//详情添加
                                    {
                                        filter.Add(url);
                                        UrlQueue.Instance.EnQueue(new UrlInfo(url) { Depth = args.urlInfo.Depth, PostData = args.urlInfo.PostData,UniqueKey=args.urlInfo.UniqueKey });
                                    }
                                }
                            }

                        }
                    }
                    //  DBChangeQueue.Instance.EnQueue(new StorageData() { Document = new BsonDocument().Add("isUpdate",1) , Name = DataTableName,Query=Query.EQ("projId", projId), Type = StorageType.Update });
                }
           }
           

        }


        private bool hasExistObj(string guid)
        {
            return (this.dataop.FindCount(this.DataTableNameHouse, Query.EQ("houseId", guid)) > 0);
        }
        private static string GetGuidFromUrl(string url)
        {
            int num = url.LastIndexOf("=");
            int num2 = url.Length;
            if ((num != -1) && (num2 != -1))
            {
                if (num > num2)
                {
                    int num3 = num;
                    num = num2;
                    num2 = num3;
                }
                return url.Substring(num + 1, (num2 - num) - 1);
            }
            return string.Empty;
        }

        public string GetInnerText(HtmlNode node)
        {
            if ((node == null) || string.IsNullOrEmpty(node.InnerText))
            {
                throw new NullReferenceException();
            }
            return node.InnerText;
        }

        private static string GetQueryString(string url)
        {
            int index = url.IndexOf("?");
            if (index != -1)
            {
                return url.Substring(index + 1, (url.Length - index) - 1);
            }
            return string.Empty;
        }

        public string[] GetStrSplited(string str)
        {
            string[] separator = new string[] { ":", "：" };
            return str.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string GetUrlParam(string url, string name)
        {
            NameValueCollection values = HttpUtility.ParseQueryString(GetQueryString(url));
            return ((values[name] != null) ? values[name].ToString() : string.Empty);
        }



        #region method
 
        /// <summary>
        /// IP限定处理，ip被限制 账号被限制跳转处理
        /// </summary>
        /// <param name="args"></param>
        public bool IPLimitProcess(DataReceivedEventArgs args)
        {
            if (string.IsNullOrEmpty(args.Html) || args.Html.Contains("503 Service Unavailable"))//需要编写被限定IP的处理
            {
                return true;
            }
            HtmlDocument htmlDoc = new HtmlDocument();
            htmlDoc.LoadHtml(args.Html);
            var ibodyDiv = htmlDoc.GetElementbyId("content");
            if (ibodyDiv == null)
            {
                
                return true;
            }
             return false;
        }
        /// <summary>
        /// url处理,是否可添加
        /// </summary>
        /// <param name="args"></param>
        /// <returns></returns>
        public bool CanAddUrl(AddUrlEventArgs args)
        {

            return true;
        }

        /// <summary>
        /// void错误处理
        /// </summary>
        /// <param name="args"></param>
        public void ErrorReceive(CrawlErrorEventArgs args)
        {


        }

        /// <summary>
        /// 模拟登陆，ip代理可能需要用到
        /// </summary>
        /// <returns></returns>
        public bool SimulateLogin()
        {
            return true;

        }



        /// <summary>
        /// ip无效处理
        /// </summary>
        private void IPInvalidProcess(IPProxy ipproxy)
        {
            Settings.SetUnviableIP(ipproxy);//设置为无效代理
            if (ipproxy != null)
            {
                DBChangeQueue.Instance.EnQueue(new StorageData()
                {
                    Name = "IPProxy",
                    Document = new BsonDocument().Add("status", "1"),
                    Query = Query.EQ("ip", ipproxy.IP)
                });
                StartDBChangeProcess();
            }

        }

        /// <summary>
        /// 对需要更新的队列数据更新操作进行批量处理,可考虑异步执行
        /// </summary>
        private void StartDBChangeProcess()
        {

            List<StorageData> updateList = new List<StorageData>();
            while (DBChangeQueue.Instance.Count > 0)
            {
                var curStorage = DBChangeQueue.Instance.DeQueue();
                if (curStorage != null)
                {
                    updateList.Add(curStorage);
                }
            }
            if (updateList.Count() > 0)
            {
                var result = dataop.BatchSaveStorageData(updateList);
                if (result.Status != Status.Successful)//出错进行重新添加处理
                {
                    foreach (var storageData in updateList)
                    {
                        DBChangeQueue.Instance.EnQueue(storageData);
                    }
                }
            }

        }
        #endregion
    }

}
