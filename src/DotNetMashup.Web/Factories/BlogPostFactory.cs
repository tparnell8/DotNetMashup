﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.ServiceModel.Syndication;
using System.Threading.Tasks;
using System.Xml;
using DotNetMashup.Web.Extensions;
using DotNetMashup.Web.Global;
using DotNetMashup.Web.Model;
using Microsoft.Framework.Caching.Memory;

namespace DotNetMashup.Web.Factories
{
    public class BlogPostFactory : IFactory
    {
        private readonly ISiteSetting setting;

        private readonly IMemoryCache cache;
        private readonly IEnumerable<MetaData> _data;
        private const string cacheKey = "blogposts";

        public BlogPostFactory(IEnumerable<MetaData> data, IMemoryCache cache, ISiteSetting setting)
        {
            this._data = data;
            this.cache = cache;
            this.setting = setting;
        }

        public string FactoryName
        {
            get
            {
                return "BlogPost";
            }
        }

        public IEnumerable<IExternalData> GetData()
        {
            var cachedata = cache.Get<IEnumerable<IExternalData>>(cacheKey);
            if(cachedata != null) return cachedata;
            var syndicationFeeds = GetSyndicationFeeds(_data);

            var data = syndicationFeeds
               .SelectMany(pair => pair.Value.Items, (pair, item) => new { Id = pair.Key, Item = item })
               .Where(x => x.Item.Categories.Any(category => setting.Categories.Any(setting => string.Equals(setting, category.Name, StringComparison.OrdinalIgnoreCase))))
               .Select(x =>
               {
                   var metaauthor = _data.First(y => y.Id == x.Id);
                   var authorname = metaauthor.Author;
                   var authoremail = metaauthor.AuthorEmail;

                   var link = x.Item.Links.FirstOrDefault(y => y.RelationshipType == "alternate");
                   var locallink = string.Empty;
                   if(link != null)
                   {
                       locallink = link.Uri.Segments.Last();
                       if(locallink.Contains("."))
                       {
                           locallink = locallink.Substring(0, locallink.IndexOf(".", System.StringComparison.Ordinal));
                       }
                   }

                   var originallink = link == null ? string.Empty : link.Uri.AbsoluteUri;

                   var summary = x.Item.Summary == null
                       ? ((TextSyndicationContent)x.Item.Content).Text
                       : x.Item.Summary.Text;

                   var truncatedSummary = summary.TruncateHtml(700, "");

                   var encodedcontent = x.Item.ElementExtensions.ReadElementExtensions<string>("encoded",
                       "http://purl.org/rss/1.0/modules/content/");

                   var content = string.Empty;

                   if(encodedcontent.Any())
                   {
                       content = encodedcontent.First();
                   }
                   else if(x.Item.Content != null)
                   {
                       content = ((TextSyndicationContent)x.Item.Content).Text;
                   }
                   else
                   {
                       content = summary;
                   }

                   return new BlogPost
                   {
                       Title = x.Item.Title.Text,
                       Summary = truncatedSummary,
                       Author = authorname,
                       AuthorEmail = authoremail,
                       Localink = locallink,
                       OriginalLink = originallink,
                       PublishedDate = x.Item.PublishDate.DateTime,
                       Content = content
                   };
               })
               .OrderByDescending(x => x.PublishedDate)
               .ToList();
            cache.Set(cacheKey, data.Cast<IExternalData>());
            return data;
        }

        private static IEnumerable<KeyValuePair<string, SyndicationFeed>> GetSyndicationFeeds(IEnumerable<MetaData> metadataEntries)
        {
            var syndicationFeeds = new List<KeyValuePair<string, SyndicationFeed>>();
            foreach(var metadata in metadataEntries)
            {
                GetFeed(metadata.FeedUrl, metadata.Id, syndicationFeeds);
            }

            return syndicationFeeds;
        }

        private static void GetFeed(string url, string id, List<KeyValuePair<string, SyndicationFeed>> syndicationFeeds)
        {
            try
            {
                SyndicationFeed feed = null;
                using(var reader = XmlReader.Create(url))
                {
                    feed = SyndicationFeed.Load(reader);
                }

                if(feed != null)
                {
                    syndicationFeeds.Add(new KeyValuePair<string, SyndicationFeed>(id, feed));
                    if(feed.Links.Any(x => x.RelationshipType == "next"))
                    {
                        foreach(var pagingLink in feed.Links.Where(x => x.RelationshipType == "next"))
                        {
                            GetFeed(pagingLink.Uri.AbsoluteUri, id, syndicationFeeds);
                        }
                    }
                }
            }
            catch(WebException)
            {
                //Unable to load RSS feed
            }
            catch(XmlException)
            {
                //Unable to load RSS feed
            }
        }
    }
}