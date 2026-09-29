function textContent(value) {
	return value
		.replace(/<see\s+cref="([^"]+)"\s*\/>/g, (_, cref) => `\`${cref.replace(/^[A-Z]:/, '')}\``)
		.replace(/<see\s+href="([^"]+)"\s*>([\s\S]*?)<\/see>/g, '[$2]($1)')
		.replace(/<(?:paramref|typeparamref)\s+name="([^"]+)"\s*\/>/g, '`$1`')
		.replace(/<c>([\s\S]*?)<\/c>/g, '`$1`')
		.replace(/<code>([\s\S]*?)<\/code>/g, '\n```\n$1\n```\n')
		.replace(/<para\s*>/g, '\n\n')
		.replace(/<\/para>/g, '\n\n')
		.replace(/<br\s*\/>/g, '\n')
		.replace(/<[^>]+>/g, '')
		.replace(/&amp;/g, '&')
		.replace(/&lt;/g, '<')
		.replace(/&gt;/g, '>')
		.replace(/&quot;/g, '"')
		.replace(/&apos;/g, "'")
		.replace(/[\t ]+\n/g, '\n')
		.replace(/\n{3,}/g, '\n\n')
		.trim();
}

function elementContent(xml, name) {
	const match = xml.match(new RegExp(`<${name}(?:\\s[^>]*)?>([\\s\\S]*?)<\\/${name}>`));
	return match ? textContent(match[1]) : '';
}

function memberKind(name) {
	return name.slice(0, 2);
}

function memberPath(name) {
	return name.slice(2);
}

function parentType(name) {
	const path = memberPath(name);
	if (memberKind(name) === 'T:') return path;
	const open = path.indexOf('(');
	const member = open < 0 ? path : path.slice(0, open);
	return member.slice(0, member.lastIndexOf('.'));
}

function displayName(name) {
	const path = memberPath(name);
	if (memberKind(name) === 'T:') return path;
	const open = path.indexOf('(');
	const member = open < 0 ? path : path.slice(0, open);
	const signature = open < 0 ? '' : path.slice(open);
	const shortName = member.slice(member.lastIndexOf('.') + 1);
	return `${shortName}${signature}`;
}

export function convertXmlDocsToMarkdown(xml) {
	const members = [...xml.matchAll(/<member\s+name="([^"]+)"\s*>([\s\S]*?)<\/member>/g)];
	const types = new Map();

	for (const [, name, body] of members) {
		const type = parentType(name);
		const member = { name, body, kind: memberKind(name) };
		if (!types.has(type)) types.set(type, []);
		types.get(type).push(member);
	}

	const sections = [];
	for (const [type, membersForType] of types) {
		sections.push(`## \`${type}\``);
		for (const member of membersForType) {
			sections.push(`### \`${displayName(member.name)}\``);
			sections.push(elementContent(member.body, 'summary') || 'No summary is available.');
			const remarks = elementContent(member.body, 'remarks');
			if (remarks) sections.push(remarks);
			const params = [...member.body.matchAll(/<param\s+name="([^"]+)"\s*>([\s\S]*?)<\/param>/g)];
			if (params.length) {
				sections.push('**Parameters**');
				for (const [, parameter, description] of params) {
					sections.push(`- \`${parameter}\`: ${textContent(description)}`);
				}
			}
			const returns = elementContent(member.body, 'returns');
			if (returns) sections.push(`**Returns:** ${returns}`);
		}
	}

	return `${sections.join('\n\n')}\n`;
}
