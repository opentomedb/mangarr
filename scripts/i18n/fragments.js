// UI translations v1 (2026-09-25): the rendered-English model of one frontend source file.
// A fragment is a piece of English the file can render: a string literal, a template literal, a
// '+' chain with a string in it, a JSX text run, or a translate()/translateElements() call
// evaluated against en.json. Expressions inside a fragment print as {name} for an identifier or
// member chain and {…} for anything else, so `${count} files`, `{count} files` in JSX and
// translate('CountFiles', { count }) with en "{count} files" all print "{count} files".
// Routing a literal through translate() must leave a file's fragment multiset unchanged.
const path = require('path');
const REPO = path.resolve(__dirname, '../..');
const parser = require(path.join(REPO, 'node_modules/@babel/parser'));
const traverse = require(path.join(REPO, 'node_modules/@babel/traverse')).default;
const t = require(path.join(REPO, 'node_modules/@babel/types'));

const TRANSLATORS = new Set(['translate', 'translateElements']);
const TOKEN_RE = /\{([a-z0-9]+?)\}/gi;
const UI_PROPS = new Set(['title', 'placeholder', 'aria-label', 'alt', 'label', 'helpText', 'helpTextWarning',
  'legend', 'message', 'text', 'tooltip', 'confirmLabel', 'cancelLabel', 'emptyText', 'header', 'body',
  'buttonText', 'description', 'errorMessage', 'heading', 'hint', 'caption', 'subtitle', 'prefix', 'suffix',
  'columnLabel', 'kindLabel']);
const UI_KEYS = new Set(['label', 'title', 'message', 'text', 'helpText', 'placeholder', 'value', 'name',
  'description', 'tooltip', 'columnLabel']);

function parse(source) {
  return parser.parse(source, {
    sourceType: 'module',
    plugins: ['jsx', 'typescript', 'classProperties', 'decorators-legacy']
  });
}

function isTranslator(node) {
  return t.isCallExpression(node) && t.isIdentifier(node.callee) && TRANSLATORS.has(node.callee.name);
}

// UI translations v1 (2026-09-25), fix round 1 (R1b): the placeholder a nested element renders as,
// shared by tokenText() (what a translateElements() token argument evaluates to) and the ordered
// composite built below (what the same element renders as in the untouched JSX it replaces).
function elementPlaceholder(node) {
  const name = node.openingElement.name;
  return `<${t.isJSXMemberExpression(name) ? `${name.object.name}.${name.property.name}` : name.name}>`;
}

function memberName(node) {
  if (t.isIdentifier(node)) {
    return node.name;
  }

  if (t.isMemberExpression(node) && !node.computed && t.isIdentifier(node.property)) {
    const object = t.isThisExpression(node.object) ? 'this' : memberName(node.object);
    return object && `${object}.${node.property.name}`;
  }

  return null;
}

// Wordy English, not an identifier, class name, path or URL (the inventory's scan_fe.js rule).
function isWordy(s) {
  const v = s.replace(/\s+/g, ' ').trim();
  return /[A-Za-z]{2,}/.test(v) && !/^[a-z][A-Za-z0-9]*$/.test(v) && !/^[A-Z_0-9]+$/.test(v) &&
    !/^(https?:|\/|#|\.|[a-z-]+\/)/.test(v) && !/^[a-z0-9-]+( [a-z0-9-]+)*$/.test(v) && !/^\{.*\}$/.test(v);
}

// UI translations v1 (2026-09-25), controller ruling S1: a `name:` property is a wire identifier,
// not UI text, when its object is passed straight to executeCommand(...) or is the
// JSON.stringify()'d body of a POST to '/command'. Both shapes still hold a raw string literal
// today -- CollectionsConnector.js's executeCommand({ name: 'MissingBookSearch', ... }) and
// ChangeEditionModalContent.js's /command body { name: 'ReResolveEdition', ... } -- which Task 4
// converts to commandNames.* constants (an identifier there renders as {name}, not a literal, and
// this skip becomes moot).
function isCommandWireName(p, parent) {
  if (!t.isObjectProperty(parent) || parent.value !== p.node || parent.computed) {
    return false;
  }

  const key = t.isIdentifier(parent.key) ? parent.key.name : parent.key.value;

  if (key !== 'name') {
    return false;
  }

  const objExpr = p.parentPath.parentPath;

  if (!objExpr || !objExpr.isObjectExpression()) {
    return false;
  }

  const call = objExpr.parentPath;

  if (!call || !call.isCallExpression()) {
    return false;
  }

  if (call.node.arguments[0] === objExpr.node) {
    const callee = memberName(call.node.callee);

    if (callee && callee.split('.').pop() === 'executeCommand') {
      return true;
    }
  }

  if (memberName(call.node.callee) === 'JSON.stringify') {
    const dataProp = call.parentPath;
    const dataKey = dataProp && t.isObjectProperty(dataProp.node) &&
      (t.isIdentifier(dataProp.node.key) ? dataProp.node.key.name : dataProp.node.key.value);

    if (dataProp && dataProp.isObjectProperty() && dataProp.node.value === call.node && dataKey === 'data') {
      const requestObj = dataProp.parentPath;
      const urlProp = requestObj && requestObj.isObjectExpression() && requestObj.node.properties.find((prop) =>
        t.isObjectProperty(prop) && (t.isIdentifier(prop.key) ? prop.key.name : prop.key.value) === 'url');

      if (urlProp && t.isStringLiteral(urlProp.value) && urlProp.value.value === '/command') {
        return true;
      }
    }
  }

  return false;
}

function extract(source, en) {
  const ast = parse(source);
  const consumed = new Set();
  const fragments = [];
  const moduleScope = [];
  const keys = [];
  const candidates = [];
  const getters = [];
  const arrows = [];
  const unfilled = [];
  const composites = [];

  // A fragment with no letters outside its placeholders ("{…}", "{count}") carries no English.
  function push(text) {
    if (/[A-Za-z]/.test(text.replace(/\{[^}]*\}/g, ''))) {
      fragments.push(text);
    }
  }

  // UI translations v1 (2026-09-25), fix round 1 (R1b): the ordered sentence a JSX parent renders,
  // spanning its nested elements (each replaced by elementPlaceholder()), not just the text run up
  // to the next one. This is what a translateElements() call must reproduce, in the same order, to
  // prove it replaced this exact sentence rather than merely reusing its pieces out of order.
  function pushComposite(text) {
    if (/<[A-Za-z][\w.]*>/.test(text) && /[A-Za-z]/.test(text.replace(/<[^>]*>/g, '').replace(/\{[^}]*\}/g, ''))) {
      composites.push(text);
    }
  }

  function render(node) {
    if (t.isStringLiteral(node)) {
      consumed.add(node);
      return node.value;
    }

    if (t.isTemplateLiteral(node)) {
      consumed.add(node);
      return node.quasis.map((q, i) => q.value.cooked + (i < node.expressions.length ? render(node.expressions[i]) : '')).join('');
    }

    if (isTranslator(node)) {
      consumed.add(node);
      return evaluate(node);
    }

    if (t.isBinaryExpression(node, { operator: '+' }) && hasString(node)) {
      consumed.add(node);
      return render(node.left) + render(node.right);
    }

    const name = memberName(node);
    return name ? `{${name}}` : '{…}';
  }

  function hasString(node) {
    if (t.isStringLiteral(node) || t.isTemplateLiteral(node) || isTranslator(node)) {
      return true;
    }

    return t.isBinaryExpression(node, { operator: '+' }) && (hasString(node.left) || hasString(node.right));
  }

  function tokenText(node) {
    if (t.isJSXElement(node)) {
      return elementPlaceholder(node);
    }

    return render(node);
  }

  // translate.ts, line for line: appName is forced, then every token value is also its own
  // position, so a value's {0} renders the first token (or "Mangarr" when none is passed).
  //
  // UI translations v1 (2026-09-25), fix round 1 (R1a): translate.ts itself falls back to the raw
  // "{token}" text when a token isn't supplied -- real users would see that literal placeholder.
  // That raw text happens to look exactly like this tool's own "{name}" convention for an
  // unrendered expression, so silently returning it here would make a routing task's mis-named
  // token invisible to the fragment diff. Record it as an `unfilled` problem instead -- that
  // explicit check is what actually catches the mismatch. The '{…}' sentinel returned in its place
  // is only the existing "anything else" fallback (a conditional inside a template renders the same
  // way); it can coincide with an unrelated real fragment, so it is not itself the proof.
  //
  // UI translations v1 (2026-09-25), Task 5: translateElements(key, elements, tokens) forwards its
  // optional third argument to translate() as the data tokens, and splits the result on the element
  // placeholders translate() left alone. So here the data tokens (and appName, and their positions)
  // fill first, exactly as translate.ts does, and the elements fill only the names still unfilled.
  function evaluate(call) {
    const [keyArg, tokensArg] = call.arguments;
    const withElements = call.callee.name === 'translateElements';
    const dataArg = withElements ? call.arguments[2] : tokensArg;

    if (!t.isStringLiteral(keyArg)) {
      return '{…}';
    }

    consumed.add(keyArg);
    keys.push({ key: keyArg.value, line: keyArg.loc.start.line });

    const value = Object.prototype.hasOwnProperty.call(en, keyArg.value) ? en[keyArg.value] : keyArg.value;
    let tokens = {};

    if (t.isArrayExpression(dataArg)) {
      tokens = dataArg.elements.map(tokenText);
    } else if (t.isObjectExpression(dataArg)) {
      dataArg.properties.forEach((p) => {
        if (t.isObjectProperty(p)) {
          tokens[t.isIdentifier(p.key) ? p.key.name : p.key.value] = tokenText(p.value);
        }
      });
    } else if (dataArg || (withElements && tokensArg && !t.isObjectExpression(tokensArg))) {
      return value.replace(TOKEN_RE, '{…}');
    }

    tokens.appName = 'Mangarr';
    Object.values(tokens).forEach((v, i) => {
      tokens[i] = v;
    });

    if (withElements && t.isObjectExpression(tokensArg)) {
      tokensArg.properties.forEach((p) => {
        const name = t.isObjectProperty(p) && (t.isIdentifier(p.key) ? p.key.name : p.key.value);

        if (name && !Object.prototype.hasOwnProperty.call(tokens, name)) {
          tokens[name] = tokenText(p.value);
        }
      });
    }

    return value.replace(TOKEN_RE, (match, token) => {
      if (Object.prototype.hasOwnProperty.call(tokens, token)) {
        return String(tokens[token]);
      }

      unfilled.push({ key: keyArg.value, token, callee: call.callee.name, line: keyArg.loc.start.line });
      return '{…}';
    });
  }

  traverse(ast, {
    CallExpression(p) {
      if (!isTranslator(p.node)) {
        return;
      }

      const fn = p.getFunctionParent();

      if (!fn) {
        moduleScope.push({ line: p.node.loc.start.line });
      } else if (fn.isObjectMethod({ kind: 'get' })) {
        getters.push({ line: p.node.loc.start.line });
      } else if (fn.isArrowFunctionExpression() && fn.node.body === p.node && fn.parentPath.isObjectProperty()) {
        const key = fn.parent.key;
        arrows.push({ line: p.node.loc.start.line, prop: t.isIdentifier(key) ? key.name : key.value });
      }
    },

    JSXElement(p) {
      // React's own whitespace rules (babel-plugin-transform-react-jsx): runs of text and
      // expressions between child elements render as one string.
      let run = [];
      const composite = [];
      const flush = () => {
        push(run.join(''));
        run = [];
      };

      t.react.buildChildren(p.node).forEach((child) => {
        if (t.isJSXElement(child)) {
          flush();
          composite.push(elementPlaceholder(child));
        } else if (t.isJSXFragment(child)) {
          flush();
        } else {
          const text = render(child);
          run.push(text);
          composite.push(text);
        }
      });
      flush();
      pushComposite(composite.join(''));
    },

    JSXFragment(p) {
      let run = [];
      const composite = [];
      t.react.buildChildren(p.node).forEach((child) => {
        if (t.isJSXElement(child)) {
          push(run.join(''));
          run = [];
          composite.push(elementPlaceholder(child));
        } else if (t.isJSXFragment(child)) {
          push(run.join(''));
          run = [];
        } else {
          const text = render(child);
          run.push(text);
          composite.push(text);
        }
      });
      push(run.join(''));
      pushComposite(composite.join(''));
    }
  });

  // Everything a JSX run did not consume stands alone: props, object values, variables.
  traverse(ast, {
    'StringLiteral|TemplateLiteral|BinaryExpression|CallExpression'(p) {
      const node = p.node;

      // A node already rendered into a fragment is consumed; a literal nested inside an expression
      // that printed as {…} (a conditional in a template, say) was not, and stands alone here.
      if (consumed.has(node) || p.parentPath.isImportDeclaration() || p.parentPath.isExportDeclaration() ||
          p.parentPath.isTSLiteralType() || (p.parentPath.isObjectProperty() && p.parentPath.node.key === node)) {
        return;
      }

      if (t.isBinaryExpression(node) && !(node.operator === '+' && hasString(node))) {
        return;
      }

      if (t.isBinaryExpression(node) && t.isBinaryExpression(p.parent, { operator: '+' })) {
        return; // the outermost '+' renders the chain
      }

      if (t.isCallExpression(node) && !isTranslator(node)) {
        return;
      }

      push(render(node));
    }
  });

  // UI-looking literals that are still hard-coded (the completeness check's input).
  traverse(ast, {
    JSXText(p) {
      if (isWordy(p.node.value)) {
        candidates.push({ line: p.node.loc.start.line, text: p.node.value.replace(/\s+/g, ' ').trim() });
      }
    },
    StringLiteral(p) {
      const parent = p.parent;
      const v = p.node.value;

      if (!isWordy(v) || p.parentPath.isImportDeclaration() || (isTranslator(parent) && parent.arguments[0] === p.node) ||
          isCommandWireName(p, parent) ||
          p.findParent((x) => x.isCallExpression() && t.isMemberExpression(x.node.callee) && x.node.callee.object.name === 'console')) {
        return;
      }

      const container = p.findParent((x) => x.isJSXExpressionContainer() || x.isFunction());
      const inUiProp = (t.isJSXAttribute(parent) && UI_PROPS.has(parent.name.name)) ||
        (container && container.isJSXExpressionContainer() &&
          (container.parentPath.isJSXElement() || container.parentPath.isJSXFragment() ||
            (container.parentPath.isJSXAttribute() && UI_PROPS.has(container.parent.name.name)))) ||
        (t.isObjectProperty(parent) && parent.value === p.node && UI_KEYS.has(parent.key.name || parent.key.value)) ||
        /^[A-Z][a-z']+(\s+\S+)+/.test(v.trim()) || /\b(is|are|the|to|of|could|not|failed|unable)\b.*\s/i.test(v);

      if (inUiProp) {
        candidates.push({ line: p.node.loc.start.line, text: v.replace(/\s+/g, ' ').trim() });
      }
    }
  });

  return { fragments, moduleScope, keys, candidates, getters, arrows, unfilled, composites };
}

module.exports = { extract, isWordy };
