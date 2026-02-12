import { Hono } from 'hono';
import type { SessionManager } from '../services/session-manager.js';
export declare function createSessionsRoute(sessionManager: SessionManager): Hono<import("hono/types").BlankEnv, import("hono/types").BlankSchema, "/">;
