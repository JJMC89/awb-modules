/********************************************************************************************
* Task   : External link template parameter remover
* Author : JJMC89
* 
* DelimitTemplates, UndelimitTemplates, and GetParameters are based on
* https://en.wikisource.org/w/index.php?title=User:Pathosbot/TemplateEditor.cs&oldid=2350656
* which is licensed under CC BY-SA 3.0 (https://creativecommons.org/licenses/by-sa/3.0/).
* All other code is licensed under the GNU GPLv3.
********************************************************************************************/

private static readonly string targetTemplate = "Template name";

private static readonly Regex targetTemplateRegex = Tools.NestedTemplateRegex("Template name,Redirect 1,Redirect 2,etc.".Split(',').ToList());

private static readonly List<string> removeParameters = "id,1".Split(',').ToList();

private static readonly List<string> pagenamebaseParameters = "name,2".Split(',').ToList();

private static readonly Dictionary<string, string> parameterRenameMap = new Dictionary<string, string>
{
	{ "1", "id" },
	{ "2", "name" }
};

private static readonly string requestOldid = "999999999";

/* Explicitly delimit templates in text */
string DelimitTemplates(string text) {
	// escape nowiki blocks
	MatchCollection escaped = Regex.Matches(text, "<\\s*(nowiki|pre)\\s*>.*?<\\s*/\\s*\\1\\s*>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
	int i = 0;
	foreach(Match match in escaped) {
		text = text.Replace(match.Value, "_bot_escape_" + i + "_");
		i++;
	}

	// delimit parameters
	text = Regex.Replace(text, "\\|", "<pipe>");
	text = Regex.Replace(text, "\\[\\[([^\\]]+)<pipe>", "[[$1|"); // unescape wikilinks

	// delimit templates
	int count = 0;
	while(Regex.IsMatch(text, "{{") && count < 10) {
		text = Regex.Replace(text, "{{([\\s\\n\\r]*([^<{}]+?)[\\s\\n\\r]*(?:<pipe>[^{}]*)?)}}", "<start $2>$1<end $2>", RegexOptions.Singleline);
		count++;
	};

	// restore nowiki blocks
	i = 0;
	foreach(Match match in escaped) {
		text = text.Replace("_bot_escape_" + i + "_", match.Value);
		i++;
	}

	// exit
	return text;
}

/* Reverse explicit template delimiting */
string UndelimitTemplates(string text) {
	text = Regex.Replace(text, "<start[^>]+>", "{{");
	text = Regex.Replace(text, "<end[^>]+>", "}}");
	text = Regex.Replace(text, "<pipe>", "|");

	return text;
}
string UndelimitTemplates(string text, string regexSearch) {
	return UndelimitTemplates(text, regexSearch, RegexOptions.None);
}
string UndelimitTemplates(string text, string regexSearch, RegexOptions options) {
	MatchCollection matches = Regex.Matches(text, regexSearch, options);

	foreach(Match match in matches) {
		text = text.Replace(match.Value, UndelimitTemplates(match.Value));
	}

	return text;
}

/* Given a delimited template, returns a dictionary of its parameters */
Dictionary<string, string> GetParameters(string text) {
	Dictionary<string, string> parameters = new Dictionary<string, string>();

	// remove main delimiters
	text = Regex.Replace(text, "^[\\r\\n\\s]*<start([^>]+)>[\\r\\n\\s]*(.+?)[\\r\\n\\s]*<end\\1>[\\r\\n\\s]*$", "$2", RegexOptions.Singleline);

	// unescape nested parameters
	text = UndelimitTemplates(text, "<start([^>]+)>.*<end\\1>", RegexOptions.Singleline);

	// normalize
	text = Regex.Replace(text, "[\\r\\n\\s]*<pipe>[\\r\\n\\s]*", "<pipe>"); // remove pipe whitespace
	text = Regex.Replace(text, "<pipe>([a-z_0-9]+?)[\\r\\n\\s]*=[\\r\\n\\s]*", "<pipe>$1=", RegexOptions.IgnoreCase); // remove parameter whitespace
	text = Regex.Replace(text, "[\\r\\n\\s]*$", ""); // remove ending whitespace

	// process each parameter
	int unnamed = 0;
	MatchCollection matches = Regex.Matches(text, "(?<=<pipe>)(.*?)(?=<pipe>|$)", RegexOptions.Singleline);
	foreach(Match match in matches) {
		// parse key/value
		string name, value;

		if(Regex.IsMatch(match.Value, "^[^=]+=", RegexOptions.IgnoreCase)) {
			name = Regex.Replace(match.Value, "=.*$", "", RegexOptions.Singleline).Trim();
			value = Regex.Replace(match.Value, "^[^=]+=", "", RegexOptions.Singleline | RegexOptions.IgnoreCase).Trim();
		}
		else {
			unnamed++;
			name = unnamed.ToString();
			value = match.Value;
		}

		// add to dict
		if(parameters.ContainsKey(name))
			parameters[name] = value;
		else
			parameters.Add(name, value);
	}

	// exit
	return parameters;
}

public string ProcessArticle(string ArticleText, string ArticleTitle, int wikiNamespace, out string Summary, out bool Skip)
{
	Skip = !Namespace.IsMainSpace(ArticleTitle);
	Summary = "Remove {{" + targetTemplate + "}} parameter(s) migrated to Wikidata per [[Special:Permalink/" + requestOldid + "#Requests|request]]";
	
	Regex regexExternalLinksSection = new Regex(@"^={1,6} *External links? *={1,6}(?: *⌊⌊⌊⌊\d{1,4}⌋⌋⌋⌋| *<!--.*?-->|< *[Bb][Rr] */ *>)?\s*$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
	Regex regexPagenamebase = new Regex(@" +\([^\)]+\)$", RegexOptions.IgnoreCase);
	
	int targetTemplateCalls = 0;
	
	string[] sections = Tools.SplitToSections(ArticleText);
	
	for (int i = sections.Length-1; i >= 0; i--) {
		
		if (regexExternalLinksSection.Match(sections[i]).Success) {
			
			foreach(Match m in targetTemplateRegex.Matches(sections[i]))
			{
				targetTemplateCalls++;
				
				string replacementTemplateCall = m.Value;
				
				// Process if there are template arguments
				if(Tools.GetTemplateArgumentCount(m.Value) > 0) {
					
					replacementTemplateCall = "{{" + targetTemplate + "}}";
					
					// Get template parameters
					Dictionary<string, string> parametersDict = GetParameters(DelimitTemplates(Tools.RemoveDuplicateTemplateParameters(m.Value)));
					
					// Reconstruct the template
					foreach(KeyValuePair<string, string> parameter in parametersDict) {
						if (!String.IsNullOrEmpty(parameter.Value)) {
							replacementTemplateCall = Tools.SetTemplateParameterValue(replacementTemplateCall, parameter.Key, parameter.Value);
						}
					}
					replacementTemplateCall = Regex.Replace(replacementTemplateCall, " *\\| *", "|", RegexOptions.Singleline);
					
					// Remove parameters
					replacementTemplateCall = Tools.RemoveTemplateParameters(replacementTemplateCall, removeParameters);
					foreach(string parameterName in pagenamebaseParameters) {
						if (Tools.GetTemplateParameterValue(replacementTemplateCall, parameterName).Equals(regexPagenamebase.Replace(ArticleTitle, ""))) {
							replacementTemplateCall = Tools.RemoveTemplateParameter(replacementTemplateCall, parameterName);
						}
					}
					
					// Rename parameters
					replacementTemplateCall = Tools.RenameTemplateParameter(replacementTemplateCall, parameterRenameMap);
					
					if(!m.Value.Equals(replacementTemplateCall)) {
						sections[i] = sections[i].Replace(m.Value, replacementTemplateCall);
					}
					
				}
			
			}
			
			break;
			
		}
		
	}
	
	// Compile new article text from sections
	string newArticleText = "";
	for (int i = 0; i < sections.Length; i++) {
		newArticleText = newArticleText + sections[i];
	}
	newArticleText = newArticleText.Trim();
	
	// Skip if the new text is the same as the original or there is not one template call
	if ( !ArticleText.Trim().Equals(newArticleText) && targetTemplateCalls == 1) {
		ArticleText = newArticleText;
	}
	else {
		Skip = true;
	}
	
	return ArticleText;
}
