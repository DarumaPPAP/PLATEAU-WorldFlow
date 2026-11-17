"""Query optional PLATEAU MCP catalog metadata without issuing Unity operations."""

import argparse
from datetime import datetime, timezone
import json
import re
import sys
from urllib.error import URLError
from urllib.request import HTTPRedirectHandler, Request, build_opener

from planning_research import ID, PlanningError, public_text, require
from static_common import parse_json

ENDPOINT = "https://api.plateauview.mlit.go.jp/mcp"
SOURCE = "https://docs.plateauview.mlit.go.jp/mcp/overview/"
PROTOCOL = "2025-03-26"
TOOLS = {"areas": "plateau_search_areas", "datasets": "plateau_search_datasets"}
BODY_LIMIT = 2 * 1024 * 1024


class NoRedirect(HTTPRedirectHandler):
    """Keep requests on the configured official host."""

    def redirect_request(self, req, fp, code, msg, headers, newurl):
        return None


def rpc_document(body, media_type, identifier):
    require(media_type in {"application/json", "text/event-stream"}, "MCP_RESPONSE_FORMAT")
    if media_type == "text/event-stream":
        messages = [parse_json("\n".join(line[5:].lstrip() for line in frame.splitlines() if line.startswith("data:")))
                    for frame in body.replace("\r\n", "\n").split("\n\n") if any(line.startswith("data:") for line in frame.splitlines())]
    else:
        messages = [parse_json(body)]
    matches = [item for item in messages if item.get("id") == identifier and item.get("jsonrpc") == "2.0"]
    require(len(matches) == 1 and "result" in matches[0] and "error" not in matches[0], "MCP_RESPONSE_FORMAT")
    return matches[0]["result"]


def rpc(opener, headers, method, params=None, identifier=None):
    body = dict(jsonrpc="2.0", method=method)
    if params is not None:
        body["params"] = params
    if identifier is not None:
        body["id"] = identifier
    request = Request(ENDPOINT, json.dumps(body).encode("utf-8"), headers, method="POST")
    with opener.open(request, timeout=10) as response:
        session = response.headers.get("Mcp-Session-Id")
        if session:
            require(bool(re.fullmatch(r"[!-~]{1,256}", session)), "MCP_RESPONSE_FORMAT")
            headers["Mcp-Session-Id"] = session
        if identifier is None:
            require(response.status in {200, 202, 204}, "MCP_RESPONSE_FORMAT")
            return None
        data = response.read(BODY_LIMIT + 1)
        require(len(data) <= BODY_LIMIT, "MCP_RESPONSE_LIMIT")
        return rpc_document(data.decode("utf-8"), response.headers.get_content_type(), identifier)


def provider_items(result, kind):
    """Discard external prose, URLs and tool instructions before returning any candidates."""
    require(not result.get("isError"), "MCP_TOOL_ERROR")
    blocks = result["content"]
    require(len(blocks) == 1 and blocks[0]["type"] == "text", "MCP_RESPONSE_FORMAT")
    document = parse_json(blocks[0]["text"])
    metadata, items = document["metadata"], document[kind]
    count, total, more = (metadata[key] for key in ("returned_count", "total_count", "has_more"))
    require(type(count) is int and type(total) is int and type(more) is bool
            and 0 <= count <= 100 and count == len(items) and total >= count and (more or total == count),
            "MCP_RESPONSE_FORMAT")
    accepted = []
    for item in items:
        identifier = item["id"]
        require(bool(ID.fullmatch(identifier)), "MCP_RESPONSE_FORMAT")
        if kind == "areas":
            code = item["code"]
            require(bool(re.fullmatch(r"[0-9]{2}|[0-9]{5}", code)), "MCP_RESPONSE_FORMAT")
            accepted.append(dict(externalId=identifier, name=public_text(item["name"]), administrativeCode=code))
        else:
            year = item["year"]
            require(year is None or type(year) is int and 1900 <= year <= 2100, "MCP_RESPONSE_FORMAT")
            accepted.append(dict(externalId=identifier, name=public_text(item["name"]), year=year))
    return dict(items=accepted, status="PARTIAL" if more else "COMPLETE", truncated=more,
                returnedCount=count, totalCount=total)


def search(kind, query):
    observed = datetime.now(timezone.utc).strftime("%Y-%m-%dT%H:%M:%SZ")
    context = dict(providerId="plateau-mcp", sourceRef=SOURCE, observedAt=observed, resultKind=kind)
    if kind == "areas":
        args = {"search_text": public_text(query)}
    else:
        require(bool(re.fullmatch(r"[0-9]{5}", query)), "INVALID_ADMINISTRATIVE_CODE")
        args = {"area_codes": [query], "shallow": True}
    headers = {"Accept": "application/json, text/event-stream", "Content-Type": "application/json"}
    opener = build_opener(NoRedirect())
    try:
        init = rpc(opener, headers, "initialize",
                   dict(protocolVersion=PROTOCOL, capabilities={}, clientInfo=dict(name="PLATEAU WorldFlow", version="1")), 1)
        require(init["protocolVersion"] == PROTOCOL and "tools" in init["capabilities"], "MCP_PROTOCOL_UNSUPPORTED")
        headers["MCP-Protocol-Version"] = PROTOCOL
        rpc(opener, headers, "notifications/initialized")
        result = rpc(opener, headers, "tools/call", dict(name=TOOLS[kind], arguments=args), 2)
        return dict(context, **provider_items(result, kind), diagnostic=None)
    except PlanningError as error:
        code = str(error)
    except (TimeoutError, URLError, OSError):
        code = "MCP_UNAVAILABLE"
    except (ValueError, KeyError, TypeError, AttributeError, RecursionError):
        code = "MCP_RESPONSE_FORMAT"
    return dict(context, items=[], status="UNKNOWN", truncated=None, returnedCount=None, totalCount=None, diagnostic=code)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("kind", choices=TOOLS)
    parser.add_argument("query")
    args = parser.parse_args()
    try:
        result = search(args.kind, args.query)
        sys.stdout.buffer.write((json.dumps(result, ensure_ascii=False) + "\n").encode("utf-8"))
        return 0
    except PlanningError as error:
        print(str(error), file=sys.stderr)
        return 1


if __name__ == "__main__":
    sys.exit(main())
