﻿using Microsoft.Office.Interop.Outlook;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Outlook = Microsoft.Office.Interop.Outlook;

namespace FinalFrontier
{
    public class Analyzer
    {
        private string[] whitelist;
        private string[] linkshorteners;
        private string[] lookalikes;
        private string[] badtlds;
        private string[] badextensions;
        private string[] docextensions;
        private string[] imgextensions;
        private string[] exeextensions;
        private string[] keywords;
        private string[] badhashessha256;
        DictionaryTools dt;
        private Dictionary<string, int> DictSenderName;
        private Dictionary<string, int> DictSenderEmail;
        private Dictionary<string, int> DictSenderCombo;
        public string result;
        private string senderNameDomainPart = "";
        private bool domainMismatch = false;
        private bool isWhitelisted = false;
        private bool isLookalike = false;
        private bool isBadTldSender = false;
        private bool senderNameContainsEmail = false;
        private bool hasLinksWithShorteners = false;
        private bool hasbadextensions = false;
        private bool hasdoubleextensions = false;
        private bool hasBadTldsInLinks = false;
        public List<CheckResult> CheckResults;
        private string senderName;
        private string senderEmailAddress;
        private string senderCombo;
        private Attachments attachments;
        public int score;
        public bool isSuspicious;
        private const string HeaderRegex = @"^(?<header_key>[-A-Za-z0-9]+)(?<seperator>:[ \t]*)" +
            "(?<header_value>([^\r\n]|\r\n[ \t]+)*)(?<terminator>\r\n)";

        public Analyzer()
        {
            try
            {
                whitelist = ConfigurationManager.AppSettings["whitelist"].Split(',');
                linkshorteners = ConfigurationManager.AppSettings["linkshorteners"].Split(',');
                lookalikes = ConfigurationManager.AppSettings["lookalikes"].Split(',');
                badtlds = ConfigurationManager.AppSettings["badtlds"].Split(',');
                badextensions = ConfigurationManager.AppSettings["badextensions"].Split(',');
                docextensions = ConfigurationManager.AppSettings["docextensions"].Split(',');
                imgextensions = ConfigurationManager.AppSettings["imgextensions"].Split(',');
                exeextensions = ConfigurationManager.AppSettings["exeextensions"].Split(',');
                keywords = ConfigurationManager.AppSettings["keywords"].Split(',');
                badhashessha256 = ConfigurationManager.AppSettings["badhashessha256"].Split(',');
            }
            catch (System.Exception)
            {
                System.Windows.Forms.MessageBox.Show("Could not read configuration file app.config");
            }

            dt = new DictionaryTools();
            string userpath = Environment.GetFolderPath(Environment.SpecialFolder.Personal);
            DictSenderName = dt.Read(userpath + "\\dict-sender-name.bin");
            DictSenderEmail = dt.Read(userpath + "\\dict-sender-email.bin");
            DictSenderCombo = dt.Read(userpath + "\\dict-sender-combo.bin");
        }

        public List<CheckResult> getSummary(MailItem mailItem)
        {
            score = 0;
            isSuspicious = false;
            CheckResults = new List<CheckResult>();            
            BodyAnalyser bodyAnalyse = new BodyAnalyser();
            result = "";

            CheckResults.AddRange(bodyAnalyse.AnalyzeBody(mailItem.HTMLBody));

            string[] receivedByArray = mailItem.Headers("Received");
            string receivedBy;

            if (receivedByArray.Length > 0)
            {
                receivedBy = receivedByArray[0];
                foreach (string entry in receivedByArray)
                {
                    string receiveDomain = getReceiveFromString(entry);
                    checkBadTld("Receive-badTLD", receiveDomain);
                }

            }
            else
                receivedBy = "";

            string senderenvelope = GetSenderSMTPAddress(mailItem);

            // check for suspicious sender
            senderName = mailItem.SenderName;
            senderEmailAddress = mailItem.SenderEmailAddress;

            string senderDomainEnvelope = "";
            if (senderenvelope != null) senderDomainEnvelope = getDomainFromMail(senderenvelope);
            string senderDomainHeader = getDomainFromMail(senderEmailAddress);

            // check if senderEmail has different domain than senderEnvelope
            if ((senderenvelope != null) & (senderDomainEnvelope != senderDomainHeader))
            {
                CheckResults.Add(new CheckResult("Meta-SenderDomainMismatch", "mismatch between sender domains of envelope and header", senderDomainEnvelope + "/" + senderDomainHeader, -40));
            }

            // check if senderName contains email address with different domain than senderEnvelope
            if ((senderName.Contains("@")) & (senderDomainEnvelope != getDomainFromMail(senderName)))
            {
                CheckResults.Add(new CheckResult("Meta-SenderNameDomainMismatch", "senderName contains email address with different domain than senderEnvelope", senderDomainEnvelope + "/" + getDomainFromMail(senderName), -50));
            }

            // check if senderEnvelope has badTLD
            checkBadTld("SenderEnvelope-badTLD", senderDomainEnvelope);

            if ((senderenvelope != null) & (senderenvelope != "") & (senderEmailAddress != senderenvelope))
            {
                CheckResults.Add(new CheckResult("Meta-SenderMismatch", "Der Absender ist evtl. gefälscht (Adresse Umschlag vs. Mail)", senderEmailAddress + "/" + senderenvelope, -50));
            }

            // TODO: if senderName and SenderEmail are equal there should not be an alert!!!

            senderCombo = senderName + "/" + senderEmailAddress;
            int senderNameAtPos = senderName.IndexOf("@");
            if ((senderNameAtPos != -1) & (!senderEmailAddress.Equals("")))
            {
                // senderName contains mail address
                senderNameDomainPart = senderName.Substring(senderNameAtPos + 1);
                CheckResults.Add(new CheckResult("Meta-SenderMismatch", "Der Absender ist evtl. gefälscht (Name soll Mailadresse suggerieren)", senderEmailAddress + "/" + senderenvelope, -20));

                if ((senderEmailAddress.IndexOf(senderNameDomainPart) == -1) & (!senderEmailAddress.Equals("")))
                {
                    // senderName contains domain different to the one in senderEmailAddress
                    domainMismatch = true;
                    CheckResults.Add(new CheckResult("Meta-SenderPhishy", "Die angezeigte Mailadresse entspricht vermutlich nicht dem tatsächlichen Absender / senderName contains email address with different domain than sender", senderEmailAddress + " / " + senderNameDomainPart, -40));
                }
            }

            checkBadTld("SenderHeader-badTLD", senderEmailAddress);

            // check for domain in whitelist
            int senderEmailAddressAtPos = senderEmailAddress.IndexOf("@");
            string senderEmailAddressDomainPart = senderEmailAddress.Substring(senderEmailAddressAtPos + 1);
            if ((whitelist.Contains(senderEmailAddressDomainPart)) & (domainMismatch == false))
            {
                CheckResults.Add(new CheckResult("Meta-SenderEmailWhitelisted", "Die angezeigte Mailadresse ist in der Whitelist", senderEmailAddress + " / " + senderNameDomainPart, 80));
            }

            // evaluate history of senderName, senderEmailAddress and their combo
            if (DictSenderName.ContainsKey(senderName))
            {
                CheckResults.Add(new CheckResult("Meta-NameNew", "Der Name (Freitext) des Absenders ist bekannt", senderName, -40));
                score += DictSenderName[senderName];
            }
            else
            {
                CheckResults.Add(new CheckResult("Meta-NameNew", "Der Name (Freitext) des Absenders ist neu", senderName, -10));
            }

            if (DictSenderEmail.ContainsKey(senderEmailAddress))
            {
                if (DictSenderEmail[senderEmailAddress] > 3)
                {
                    CheckResults.Add(new CheckResult("Meta-SenderAddressSeenBefore", "Die vermeintliche Emailadresse ist bekannt.", senderEmailAddress, -30));
                }
            }
            else
            {
                CheckResults.Add(new CheckResult("Meta-SenderNew", "Vermeintliche Emailadresse ist neu.", senderEmailAddress, -20));
            }

            if (DictSenderCombo.ContainsKey(senderCombo))
            {
                if (DictSenderCombo[senderCombo] > 3)
                {
                    CheckResults.Add(new CheckResult("Meta-ComboSeenBefore", "Die Kombination von Absender (Freitext) und Emailadresse ist bekannt.", senderEmailAddress, 100));
                }
            }
            else
            {
                CheckResults.Add(new CheckResult("Meta-ComboNew", "Die Kombination von Absender (Freitext) und Emailadresse ist neu.", senderEmailAddress, -40));
            }

            attachments = mailItem.Attachments;
            //Debug.WriteLine(attachments.Count + " attachments.");
            foreach (Attachment attachment in attachments)
            {
                checkDoubleExtensions("Attachment-DoubleExtensions", attachment.FileName);

                checkBadExtensions("Attachment-BadExtension", attachment.FileName);

                checkKeywords("Attachment-Keyword", attachment.FileName);

                checkBadHashes("Attachment-FileHash", attachment);
            }

            Debug.WriteLine("---CHECK RESULTS---");
            foreach (CheckResult cr in CheckResults)
            {
                Debug.WriteLine(cr.id + " / " + cr.ioc + " / " + cr.fragment + " / " + cr.score);
                result += cr.id + " / " + cr.ioc + " / " + cr.fragment + " / " + cr.score + Environment.NewLine;
                score += cr.score;
            }
            result = "SCORE: " + score + Environment.NewLine + result;

            return CheckResults;
        }

        private void checkBadTld(string id, string instr)
        {
            if (instr == null) return;
            foreach (string badtld in badtlds)
            {
                if (instr.EndsWith(badtld))
                {
                    CheckResults.Add(new CheckResult(id, badtld, instr, -20));
                }
            }
        }

        private void checkBadHashes(String id, Attachment testfile)
        {
            if (testfile == null) return;

            String userpath = Environment.GetFolderPath(Environment.SpecialFolder.InternetCache);
            testfile.SaveAsFile(userpath + "\\testfile");
            FileStream stream = File.OpenRead(userpath + "\\testfile");
            var sha = new SHA256Managed();
            byte[] filehash = sha.ComputeHash(stream);
            String filehashstr = BitConverter.ToString(filehash).Replace("-", String.Empty);
            File.Delete(userpath + "\\testfile");
            //Debug.WriteLine(filehashstr);

            if (badhashessha256.Contains(filehashstr))
            {
                CheckResults.Add(new CheckResult(id, "sha256", filehashstr, -100));
            }
        }

        private void checkBadExtensions(String id, String instr)
        {
            foreach (string ext in badextensions)
            {
                if (instr.EndsWith(ext))
                {
                    CheckResults.Add(new CheckResult(id, ext, instr, -20));
                }
            }
        }

        private void checkKeywords(string id, string instr)
        {
            foreach (string key in keywords)
            {
                if (instr.EndsWith(key))
                {
                    CheckResults.Add(new CheckResult(id, key, instr, -20));
                }
            }
        }

        private void checkLinkShorteners(string id, string instr)
        {
            foreach (string shortener in linkshorteners)
            {
                if (instr.IndexOf(shortener) > 0)
                {
                    CheckResults.Add(new CheckResult(id, shortener, instr, -20));
                }
            }
        }

        private void checkDoubleExtensions(string id, string instr)
        {
            foreach (string docext in docextensions)
            {
                foreach (string exeext in exeextensions)
                {
                    if (instr.EndsWith(docext + exeext))
                    {
                        CheckResults.Add(new CheckResult(id, docext + exeext, instr, -20));
                    }
                }
            }
        }

        private string getReceiveFromString(string inline)
        {
            string res = "";
            try
            {
                int startpos = inline.IndexOf("from ") + 5;
                int endpos = inline.Substring(startpos).IndexOf(" ");
                res = inline.Substring(startpos, endpos);
            }
            catch (System.Exception ex)
            {
                Debug.Write(ex.StackTrace);
            }
            return res;
        }

        private string getDomainFromMail(string inval)
        {
            string res = "";
            try
            {
                int startpos = inval.IndexOf("@") + 1;
                res = inval.Substring(startpos);
            }
            catch (System.Exception ex)
            {
                Debug.Write(ex.StackTrace);
            }
            return res;
        }

        private string GetSenderSMTPAddress(Outlook.MailItem mail)
        {

            string PR_SMTP_ADDRESS = @"http://schemas.microsoft.com/mapi/proptag/0x39FE001E";
            if (mail == null)
            {
                throw new ArgumentNullException();
            }
            if (mail.SenderEmailType == "EX")
            {
                Outlook.AddressEntry sender = mail.Sender;
                if (sender != null)
                {
                    //Now we have an AddressEntry representing the Sender
                    if (sender.AddressEntryUserType == OlAddressEntryUserType.olExchangeUserAddressEntry || 
                        sender.AddressEntryUserType == OlAddressEntryUserType.olExchangeRemoteUserAddressEntry)
                    {
                        //Use the ExchangeUser object PrimarySMTPAddress
                        ExchangeUser exchUser = sender.GetExchangeUser();
                        if (exchUser != null)
                        {
                            return exchUser.PrimarySmtpAddress;
                        }
                        else
                        {
                            return null;
                        }
                    }
                    else
                    {
                        return sender.PropertyAccessor.GetProperty(PR_SMTP_ADDRESS) as string;
                    }
                }
                else
                {
                    return null;
                }
            }
            else
            {
                return null;// mail.SenderEmailAddress;
            }
        }
    }
}
