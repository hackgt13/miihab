#!/usr/bin/env python3
"""Call the installed Unity MCP stdio server when native task tools aren't exposed.

The server performs editor discovery/authentication. This client uses only MCP
initialize, tools/list and tools/call; it does not call private editor endpoints.
"""
import argparse
import asyncio
import json
from pathlib import Path

PROJECT = Path(__file__).resolve().parents[1] / 'unity' / 'KinestheticUnity'
CLI = Path.home() / '.unity' / 'bin' / 'unity'

async def run(options):
    process = await asyncio.create_subprocess_exec(
        str(CLI), 'mcp', '--project-path', str(PROJECT),
        stdin=asyncio.subprocess.PIPE, stdout=asyncio.subprocess.PIPE,
        stderr=asyncio.subprocess.DEVNULL, limit=16 * 1024 * 1024)
    async def send(message):
        process.stdin.write((json.dumps(message) + '\n').encode())
        await process.stdin.drain()
    async def request(identifier, method, params):
        await send({'jsonrpc': '2.0', 'id': identifier, 'method': method, 'params': params})
        async def receive():
            while True:
                line = await process.stdout.readline()
                if not line:
                    raise RuntimeError('Unity MCP server closed before responding.')
                try:
                    message = json.loads(line)
                except json.JSONDecodeError:
                    continue
                if message.get('id') == identifier:
                    if 'error' in message:
                        raise RuntimeError(json.dumps(message['error']))
                    return message.get('result')
        return await asyncio.wait_for(receive(), timeout=55)
    try:
        await request(1, 'initialize', {'protocolVersion': '2024-11-05',
            'capabilities': {}, 'clientInfo': {'name': 'kinesthetic-local-verifier', 'version': '0.1'}})
        await send({'jsonrpc': '2.0', 'method': 'notifications/initialized'})
        if options.call:
            arguments = json.loads(Path(options.args_file).read_text()) if options.args_file else {}
            result = await request(2, 'tools/call', {'name': options.call, 'arguments': arguments})
        else:
            result = await request(2, 'tools/list', {})
        print(json.dumps(result, indent=2))
    finally:
        if process.returncode is None:
            process.terminate()
            try:
                await asyncio.wait_for(process.wait(), timeout=3)
            except asyncio.TimeoutError:
                process.kill()
                await process.wait()

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--call')
    parser.add_argument('--args-file')
    asyncio.run(run(parser.parse_args()))
